using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BidMatrix.Application.Analyses;
using BidMatrix.Application.Audit;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace BidMatrix.Infrastructure.Analyses;

public sealed class PostgresAnalysisExtractionService(
    NpgsqlDataSource dataSource,
    IObjectStorage objectStorage,
    IDocumentTextExtractor textExtractor,
    IRequirementDetector requirementDetector,
    IAnalysisFindingDetector findingDetector,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IAnalysisExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AnalysisExtractionSnapshot> ExtractAsync(
        ExtractAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        var source = await LoadSourceAsync(command.OrganizationId, command.AnalysisId, cancellationToken);
        if (source is null)
        {
            throw NotFound(command.AnalysisId);
        }

        if (source.AnalysisStatus is not ("processing" or "requires_review"))
        {
            throw InvalidState(command.AnalysisId, source.AnalysisStatus, "extracted");
        }

        var extractionVersion = $"{textExtractor.Version}+{requirementDetector.Version}+{findingDetector.Version}";
        if (source.ExtractionStatus is "succeeded" or "partial" &&
            string.Equals(source.ExtractionVersion, extractionVersion, StringComparison.Ordinal))
        {
            return await GetRequiredAsync(command.OrganizationId, command.AnalysisId, cancellationToken);
        }

        var processedFiles = new List<ProcessedFile>(source.Files.Count);
        var allPages = new List<RequirementSourcePage>();
        foreach (var file in source.Files)
        {
            var content = await objectStorage.GetAsync(file.StorageBucket, file.StorageKey, cancellationToken);
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(content.Span));
            if (!string.Equals(actualHash, file.Sha256, StringComparison.Ordinal))
            {
                processedFiles.Add(ProcessedFile.Failed(file, "object_integrity_mismatch"));
                continue;
            }

            try
            {
                var extracted = textExtractor.Extract(content);
                var pages = extracted.Pages.Select(page => new RequirementSourcePage(
                    file.Id,
                    file.OriginalFileName,
                    page.PageNumber,
                    page.Text)).ToArray();
                allPages.AddRange(pages);
                var extractionStatus = pages.Any(page => !string.IsNullOrWhiteSpace(page.Text))
                    ? "extracted"
                    : "requires_ocr";
                processedFiles.Add(new ProcessedFile(
                    file,
                    extractionStatus,
                    requirementDetector.Classify(pages),
                    pages,
                    null));
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                processedFiles.Add(ProcessedFile.Failed(file, "pdf_text_extraction_failed"));
            }
        }

        var requirements = requirementDetector.Detect(allPages);
        var findings = findingDetector.Detect(allPages);
        await PersistAsync(command, extractionVersion, processedFiles, requirements, findings, cancellationToken);
        return await GetRequiredAsync(command.OrganizationId, command.AnalysisId, cancellationToken);
    }

    public async Task<AnalysisExtractionSnapshot?> GetAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        HeaderRow? header;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                select status, extraction_status, extraction_version, extraction_completed_at,
                    started_at, reviewed_at, published_at, review_note, correction_count
                from analyses where id = $1
                """;
            command.Parameters.AddWithValue(analysisId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            header = await reader.ReadAsync(cancellationToken)
                ? new HeaderRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    NullableString(reader, 2),
                    NullableDateTimeOffset(reader, 3),
                    NullableDateTimeOffset(reader, 4),
                    NullableDateTimeOffset(reader, 5),
                    NullableDateTimeOffset(reader, 6),
                    NullableString(reader, 7),
                    reader.GetInt32(8))
                : null;
        }

        if (header is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var documents = await LoadDocumentsAsync(connection, transaction, analysisId, cancellationToken);
        var requirements = await LoadRequirementsAsync(connection, transaction, analysisId, cancellationToken);
        var findings = await LoadFindingsAsync(connection, transaction, analysisId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var pendingReviewCount = requirements.Count(item => item.ReviewStatus == "pending") +
            findings.Count(item => item.ReviewStatus == "pending");
        var metrics = new AnalysisExtractionMetrics(
            documents.Count,
            documents.Sum(document => document.PageCount ?? 0),
            requirements.Count(requirement => requirement.ReviewStatus != "rejected"),
            requirements.Count(requirement => requirement.ReviewStatus != "rejected" && requirement.Mandatory),
            requirements.Count(requirement => requirement.ReviewStatus != "rejected" && requirement.Citations.Count > 0),
            findings.Count(finding => finding.FindingType == "key_date" && finding.ReviewStatus != "rejected"),
            findings.Count(finding => finding.FindingType == "requested_document" && finding.ReviewStatus != "rejected"),
            findings.Count(finding => finding.FindingType == "evaluation_criterion" && finding.ReviewStatus != "rejected"),
            pendingReviewCount,
            documents.Count(document => document.ExtractionStatus == "requires_ocr"),
            documents.Count(document => document.ExtractionStatus == "failed"));
        var duration = header.StartedAt is null || header.ExtractionCompletedAt is null
            ? null
            : (long?)(header.ExtractionCompletedAt.Value - header.StartedAt.Value).TotalMilliseconds;
        return new AnalysisExtractionSnapshot(
            analysisId,
            header.ExtractionStatus,
            header.ExtractionVersion,
            header.ExtractionCompletedAt,
            documents,
            requirements,
            findings,
            new AnalysisPublicationRecord(
                header.AnalysisStatus,
                header.ReviewedAt,
                header.PublishedAt,
                header.ReviewNote,
                header.CorrectionCount,
                duration,
                header.AnalysisStatus == "completed" && header.PublishedAt is not null),
            metrics);
    }

    public async Task<AnalysisExtractionSnapshot> ReviewRequirementAsync(
        ReviewRequirementCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateReview(command.RequirementText, command.Category, command.ReviewStatus, command.CorrectionNote);
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);
        await EnsureReviewableAsync(connection, transaction, command.AnalysisId, cancellationToken);

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analysis_requirements
                set requirement_text = $1,
                    normalized_requirement = $2,
                    category = $3,
                    mandatory = $4,
                    review_status = $5,
                    correction_note = $6,
                    reviewed_by_user_id = $7,
                    reviewed_at = $8,
                    updated_at = $8,
                    version = version + 1
                where id = $9 and analysis_id = $10 and version = $11
                """;
            update.Parameters.AddWithValue(command.RequirementText.Trim());
            update.Parameters.AddWithValue(DeterministicRequirementDetector.NormalizeRequirement(command.RequirementText));
            update.Parameters.AddWithValue(command.Category.Trim());
            update.Parameters.AddWithValue(command.Mandatory);
            update.Parameters.AddWithValue(command.ReviewStatus);
            update.Parameters.AddWithValue((object?)NormalizeOptional(command.CorrectionNote) ?? DBNull.Value);
            update.Parameters.AddWithValue(command.OwnerUserId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(command.RequirementId);
            update.Parameters.AddWithValue(command.AnalysisId);
            update.Parameters.AddWithValue(command.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw VersionConflict("requirement");
            }
        }

        await RefreshCorrectionCountAsync(connection, transaction, command.AnalysisId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await WriteReviewAuditAsync(command.OrganizationId, command.OwnerUserId, command.AnalysisId,
            "analysis.requirement_reviewed", $"Requirement {command.RequirementId} was {command.ReviewStatus}.",
            command.CorrelationId, cancellationToken);
        return await GetRequiredAsync(command.OrganizationId, command.AnalysisId, cancellationToken);
    }

    public async Task<AnalysisExtractionSnapshot> ReviewFindingAsync(
        ReviewFindingCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateReview(command.Detail, command.Title, command.ReviewStatus, command.CorrectionNote);
        if (command.WeightPercent is < 0 or > 100)
        {
            throw InvalidRequest("Finding weight must be between 0 and 100 percent.");
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);
        await EnsureReviewableAsync(connection, transaction, command.AnalysisId, cancellationToken);

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analysis_findings
                set title = $1,
                    detail = $2,
                    date_value = $3,
                    weight_percent = $4,
                    review_status = $5,
                    correction_note = $6,
                    reviewed_by_user_id = $7,
                    reviewed_at = $8,
                    updated_at = $8,
                    version = version + 1
                where id = $9 and analysis_id = $10 and version = $11
                """;
            update.Parameters.AddWithValue(command.Title.Trim());
            update.Parameters.AddWithValue(command.Detail.Trim());
            update.Parameters.AddWithValue((object?)command.DateValue ?? DBNull.Value);
            update.Parameters.AddWithValue((object?)command.WeightPercent ?? DBNull.Value);
            update.Parameters.AddWithValue(command.ReviewStatus);
            update.Parameters.AddWithValue((object?)NormalizeOptional(command.CorrectionNote) ?? DBNull.Value);
            update.Parameters.AddWithValue(command.OwnerUserId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(command.FindingId);
            update.Parameters.AddWithValue(command.AnalysisId);
            update.Parameters.AddWithValue(command.ExpectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw VersionConflict("finding");
            }
        }

        await RefreshCorrectionCountAsync(connection, transaction, command.AnalysisId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await WriteReviewAuditAsync(command.OrganizationId, command.OwnerUserId, command.AnalysisId,
            "analysis.finding_reviewed", $"Finding {command.FindingId} was {command.ReviewStatus}.",
            command.CorrelationId, cancellationToken);
        return await GetRequiredAsync(command.OrganizationId, command.AnalysisId, cancellationToken);
    }

    public async Task<AnalysisExtractionSnapshot> PublishAsync(
        PublishAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Confirmation != "PUBLISH REVIEWED ANALYSIS")
        {
            throw InvalidRequest("The exact publish confirmation is required.");
        }

        if (string.IsNullOrWhiteSpace(command.ReviewNote) || command.ReviewNote.Trim().Length is < 10 or > 2_000)
        {
            throw InvalidRequest("A review note between 10 and 2,000 characters is required.");
        }

        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);
        await EnsureReviewableAsync(connection, transaction, command.AnalysisId, cancellationToken);

        await ExecuteReviewAcceptanceAsync(connection, transaction, command.AnalysisId, command.OwnerUserId, now, cancellationToken);
        await RefreshCorrectionCountAsync(connection, transaction, command.AnalysisId, now, cancellationToken);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analyses
                set status = 'completed',
                    requires_human_review = false,
                    reviewed_by_user_id = $1,
                    reviewed_at = $2,
                    published_at = $2,
                    review_note = $3,
                    completed_at = $2,
                    updated_at = $2,
                    version = version + 1
                where id = $4 and status = 'requires_review'
                  and extraction_status in ('succeeded', 'partial')
                """;
            update.Parameters.AddWithValue(command.OwnerUserId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(command.ReviewNote.Trim());
            update.Parameters.AddWithValue(command.AnalysisId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw InvalidState(command.AnalysisId, "unknown", "published");
            }
        }

        await InsertPublicationEventAsync(connection, transaction, command, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await WriteReviewAuditAsync(command.OrganizationId, command.OwnerUserId, command.AnalysisId,
            "analysis.published", "Owner reviewed and published the F2 analysis to the customer.",
            command.CorrelationId, cancellationToken);
        return await GetRequiredAsync(command.OrganizationId, command.AnalysisId, cancellationToken);
    }

    private async Task PersistAsync(
        ExtractAnalysisCommand command,
        string extractionVersion,
        IReadOnlyList<ProcessedFile> processedFiles,
        IReadOnlyList<DetectedRequirement> detectedRequirements,
        IReadOnlyList<DetectedFinding> detectedFindings,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var extractionStatus = processedFiles.All(file => file.ExtractionStatus == "extracted")
            ? "succeeded"
            : "partial";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);
        await EnsureExtractableAsync(connection, transaction, command.AnalysisId, cancellationToken);

        await ExecuteAsync(connection, transaction, "delete from analysis_findings where analysis_id = $1", command.AnalysisId, cancellationToken);
        await ExecuteAsync(connection, transaction, "delete from analysis_citations where analysis_id = $1", command.AnalysisId, cancellationToken);
        await ExecuteAsync(connection, transaction, "delete from analysis_requirements where analysis_id = $1", command.AnalysisId, cancellationToken);
        await ExecuteAsync(connection, transaction, "delete from analysis_pages where analysis_id = $1", command.AnalysisId, cancellationToken);

        foreach (var processedFile in processedFiles)
        {
            await PersistFileAsync(connection, transaction, command, processedFile, now, cancellationToken);
        }

        foreach (var group in detectedRequirements.GroupBy(
                     requirement => requirement.NormalizedRequirement,
                     StringComparer.Ordinal))
        {
            await PersistRequirementAsync(connection, transaction, command, group, now, cancellationToken);
        }

        foreach (var finding in detectedFindings)
        {
            await PersistFindingAsync(connection, transaction, command, finding, now, cancellationToken);
        }

        await using (var updateAnalysis = connection.CreateCommand())
        {
            updateAnalysis.Transaction = transaction;
            updateAnalysis.CommandText = """
                update analyses
                set extraction_status = $1,
                    extraction_version = $2,
                    extraction_completed_at = $3,
                    requires_human_review = true,
                    reviewed_by_user_id = null,
                    reviewed_at = null,
                    published_at = null,
                    review_note = null,
                    correction_count = 0,
                    updated_at = $3,
                    version = version + 1
                where id = $4
                """;
            updateAnalysis.Parameters.AddWithValue(extractionStatus);
            updateAnalysis.Parameters.AddWithValue(extractionVersion);
            updateAnalysis.Parameters.AddWithValue(now);
            updateAnalysis.Parameters.AddWithValue(command.AnalysisId);
            await updateAnalysis.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertExtractionEventAsync(
            connection,
            transaction,
            command,
            extractionStatus,
            processedFiles.Sum(file => file.Pages.Count),
            detectedRequirements.Select(requirement => requirement.NormalizedRequirement).Distinct().Count(),
            detectedFindings.Count,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditWriter.AppendAsync(
            new AuditEventWrite(
                "service",
                "analysis-intake-workflow",
                "analysis.extraction_completed",
                "analysis",
                command.AnalysisId.ToString(),
                command.OrganizationId,
                command.CorrelationId,
                null,
                "F2 digital PDF extraction completed and remains pending owner review.",
                JsonSerializer.Serialize(new
                {
                    extractionStatus,
                    documentCount = processedFiles.Count,
                    pageCount = processedFiles.Sum(file => file.Pages.Count),
                    requirementCount = detectedRequirements.Select(requirement => requirement.NormalizedRequirement).Distinct().Count(),
                    findingCount = detectedFindings.Count,
                }, JsonOptions),
                now),
            cancellationToken);
    }

    private async Task<SourceState?> LoadSourceAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        string analysisStatus;
        string extractionStatus;
        string? extractionVersion;
        await using (var analysis = connection.CreateCommand())
        {
            analysis.Transaction = transaction;
            analysis.CommandText = "select status, extraction_status, extraction_version from analyses where id = $1";
            analysis.Parameters.AddWithValue(analysisId);
            await using var reader = await analysis.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            analysisStatus = reader.GetString(0);
            extractionStatus = reader.GetString(1);
            extractionVersion = NullableString(reader, 2);
        }

        var files = new List<SourceFile>();
        await using (var fileCommand = connection.CreateCommand())
        {
            fileCommand.Transaction = transaction;
            fileCommand.CommandText = """
                select id, original_file_name, sha256, storage_bucket, storage_key
                from analysis_files
                where analysis_id = $1
                order by created_at
                """;
            fileCommand.Parameters.AddWithValue(analysisId);
            await using var reader = await fileCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                files.Add(new SourceFile(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new SourceState(analysisStatus, extractionStatus, extractionVersion, files);
    }

    private async Task<AnalysisExtractionSnapshot> GetRequiredAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken) =>
        await GetAsync(organizationId, analysisId, cancellationToken) ?? throw NotFound(analysisId);

    private static async Task<IReadOnlyList<AnalysisDocumentRecord>> LoadDocumentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var documents = new List<AnalysisDocumentRecord>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select id, original_file_name, extraction_status, document_type, page_count,
                extraction_method, extraction_failure_code
            from analysis_files
            where analysis_id = $1
            order by created_at
            """;
        command.Parameters.AddWithValue(analysisId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new AnalysisDocumentRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                NullableString(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                NullableString(reader, 5),
                NullableString(reader, 6)));
        }

        return documents;
    }

    private static async Task<IReadOnlyList<AnalysisRequirementRecord>> LoadRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var requirements = new List<RequirementRow>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                select id, requirement_code, requirement_text, original_requirement_text,
                    normalized_requirement, category, mandatory, requested_evidence, confidence,
                    review_status, correction_note, version
                from analysis_requirements
                where analysis_id = $1
                order by mandatory desc, category, created_at
                """;
            command.Parameters.AddWithValue(analysisId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                requirements.Add(new RequirementRow(
                    reader.GetGuid(0),
                    NullableString(reader, 1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    !reader.IsDBNull(6) && reader.GetBoolean(6),
                    NullableString(reader, 7),
                    reader.IsDBNull(8) ? 0m : reader.GetDecimal(8),
                    reader.GetString(9),
                    NullableString(reader, 10),
                    reader.GetInt32(11)));
            }
        }

        var citations = new Dictionary<Guid, List<AnalysisCitationRecord>>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                select citation.id, citation.requirement_id, citation.analysis_file_id,
                    file.original_file_name, citation.page_number, citation.section_text,
                    citation.quote_text
                from analysis_citations citation
                join analysis_files file on file.id = citation.analysis_file_id
                where citation.analysis_id = $1
                  and citation.requirement_id is not null
                  and citation.page_number is not null
                  and citation.quote_text is not null
                order by citation.page_number, citation.created_at
                """;
            command.Parameters.AddWithValue(analysisId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var requirementId = reader.GetGuid(1);
                if (!citations.TryGetValue(requirementId, out var items))
                {
                    items = [];
                    citations[requirementId] = items;
                }

                items.Add(new AnalysisCitationRecord(
                    reader.GetGuid(0), reader.GetGuid(2), reader.GetString(3), reader.GetInt32(4),
                    NullableString(reader, 5), reader.GetString(6)));
            }
        }

        return requirements.Select(requirement => new AnalysisRequirementRecord(
            requirement.Id,
            requirement.RequirementCode,
            requirement.RequirementText,
            requirement.OriginalRequirementText,
            requirement.NormalizedRequirement,
            requirement.Category,
            requirement.Mandatory,
            requirement.RequestedEvidence,
            requirement.Confidence,
            requirement.ReviewStatus,
            requirement.CorrectionNote,
            requirement.Version,
            citations.GetValueOrDefault(requirement.Id) ?? [])).ToArray();
    }

    private static async Task<IReadOnlyList<AnalysisFindingRecord>> LoadFindingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var findings = new List<AnalysisFindingRecord>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select finding.id, finding.finding_type, finding.title, finding.detail,
                finding.original_detail, finding.date_value, finding.weight_percent,
                finding.confidence, finding.review_status, finding.correction_note,
                finding.version, finding.analysis_file_id, file.original_file_name,
                finding.page_number, finding.section_text, finding.quote_text
            from analysis_findings finding
            join analysis_files file on file.id = finding.analysis_file_id
            where finding.analysis_id = $1
            order by finding.finding_type, finding.date_value nulls last, finding.created_at
            """;
        command.Parameters.AddWithValue(analysisId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var findingId = reader.GetGuid(0);
            findings.Add(new AnalysisFindingRecord(
                findingId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                NullableDateTimeOffset(reader, 5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetString(8),
                NullableString(reader, 9),
                reader.GetInt32(10),
                new AnalysisCitationRecord(
                    findingId,
                    reader.GetGuid(11),
                    reader.GetString(12),
                    reader.GetInt32(13),
                    NullableString(reader, 14),
                    reader.GetString(15))));
        }

        return findings;
    }

    private async Task PersistFileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ExtractAnalysisCommand command,
        ProcessedFile processedFile,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var page in processedFile.Pages)
        {
            await using var insertPage = connection.CreateCommand();
            insertPage.Transaction = transaction;
            insertPage.CommandText = """
                insert into analysis_pages (
                    id, organization_id, analysis_id, analysis_file_id, page_number,
                    text_content, text_sha256, extraction_method, created_at
                ) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                """;
            insertPage.Parameters.AddWithValue(Guid.CreateVersion7());
            insertPage.Parameters.AddWithValue(command.OrganizationId);
            insertPage.Parameters.AddWithValue(command.AnalysisId);
            insertPage.Parameters.AddWithValue(processedFile.Source.Id);
            insertPage.Parameters.AddWithValue(page.PageNumber);
            insertPage.Parameters.AddWithValue(page.Text);
            insertPage.Parameters.AddWithValue(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(page.Text))));
            insertPage.Parameters.AddWithValue(textExtractor.Version);
            insertPage.Parameters.AddWithValue(now);
            await insertPage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var updateFile = connection.CreateCommand();
        updateFile.Transaction = transaction;
        updateFile.CommandText = """
            update analysis_files
            set extraction_status = $1, document_type = $2, page_count = $3,
                extraction_method = $4, extraction_failure_code = $5,
                extracted_at = $6, updated_at = $6
            where id = $7
            """;
        updateFile.Parameters.AddWithValue(processedFile.ExtractionStatus);
        updateFile.Parameters.AddWithValue((object?)processedFile.DocumentType ?? DBNull.Value);
        updateFile.Parameters.AddWithValue(processedFile.Pages.Count);
        updateFile.Parameters.AddWithValue(textExtractor.Version);
        updateFile.Parameters.AddWithValue((object?)processedFile.FailureCode ?? DBNull.Value);
        updateFile.Parameters.AddWithValue(now);
        updateFile.Parameters.AddWithValue(processedFile.Source.Id);
        await updateFile.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PersistRequirementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ExtractAnalysisCommand command,
        IGrouping<string, DetectedRequirement> group,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var representative = group.OrderByDescending(requirement => requirement.Confidence).First();
        var requirementId = Guid.CreateVersion7();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into analysis_requirements (
                    id, organization_id, analysis_id, requirement_code, requirement_text,
                    original_requirement_text, normalized_requirement, category, mandatory,
                    requested_evidence, confidence, review_status, created_at, updated_at, version
                ) values ($1, $2, $3, $4, $5, $5, $6, $7, $8, $9, $10, 'pending', $11, $11, 1)
                """;
            insert.Parameters.AddWithValue(requirementId);
            insert.Parameters.AddWithValue(command.OrganizationId);
            insert.Parameters.AddWithValue(command.AnalysisId);
            insert.Parameters.AddWithValue((object?)representative.RequirementCode ?? DBNull.Value);
            insert.Parameters.AddWithValue(representative.RequirementText);
            insert.Parameters.AddWithValue(representative.NormalizedRequirement);
            insert.Parameters.AddWithValue(representative.Category);
            insert.Parameters.AddWithValue(group.Any(requirement => requirement.Mandatory));
            insert.Parameters.AddWithValue((object?)representative.RequestedEvidence ?? DBNull.Value);
            insert.Parameters.AddWithValue(group.Max(requirement => requirement.Confidence));
            insert.Parameters.AddWithValue(now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var citation in group.DistinctBy(item => (item.AnalysisFileId, item.PageNumber, item.QuoteText)))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into analysis_citations (
                    id, organization_id, analysis_id, requirement_id, analysis_file_id,
                    page_number, section_text, quote_text, bounding_data, created_at
                ) values ($1, $2, $3, $4, $5, $6, $7, $8, null, $9)
                """;
            insert.Parameters.AddWithValue(Guid.CreateVersion7());
            insert.Parameters.AddWithValue(command.OrganizationId);
            insert.Parameters.AddWithValue(command.AnalysisId);
            insert.Parameters.AddWithValue(requirementId);
            insert.Parameters.AddWithValue(citation.AnalysisFileId);
            insert.Parameters.AddWithValue(citation.PageNumber);
            insert.Parameters.AddWithValue((object?)citation.SectionText ?? DBNull.Value);
            insert.Parameters.AddWithValue(citation.QuoteText);
            insert.Parameters.AddWithValue(now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task PersistFindingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ExtractAnalysisCommand command,
        DetectedFinding finding,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            insert into analysis_findings (
                id, organization_id, analysis_id, finding_type, title, detail,
                original_detail, normalized_value, date_value, weight_percent,
                confidence, review_status, analysis_file_id, page_number,
                section_text, quote_text, created_at, updated_at, version
            ) values ($1, $2, $3, $4, $5, $6, $6, $7, $8, $9, $10,
                'pending', $11, $12, $13, $14, $15, $15, 1)
            on conflict (analysis_id, finding_type, normalized_value, analysis_file_id, page_number) do nothing
            """;
        insert.Parameters.AddWithValue(Guid.CreateVersion7());
        insert.Parameters.AddWithValue(command.OrganizationId);
        insert.Parameters.AddWithValue(command.AnalysisId);
        insert.Parameters.AddWithValue(finding.FindingType);
        insert.Parameters.AddWithValue(finding.Title);
        insert.Parameters.AddWithValue(finding.Detail);
        insert.Parameters.AddWithValue(finding.NormalizedValue);
        insert.Parameters.AddWithValue((object?)finding.DateValue ?? DBNull.Value);
        insert.Parameters.AddWithValue((object?)finding.WeightPercent ?? DBNull.Value);
        insert.Parameters.AddWithValue(finding.Confidence);
        insert.Parameters.AddWithValue(finding.AnalysisFileId);
        insert.Parameters.AddWithValue(finding.PageNumber);
        insert.Parameters.AddWithValue((object?)finding.SectionText ?? DBNull.Value);
        insert.Parameters.AddWithValue(finding.QuoteText);
        insert.Parameters.AddWithValue(now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureExtractableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select status from analyses where id = $1 for update";
        command.Parameters.AddWithValue(analysisId);
        var status = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (status is null) throw NotFound(analysisId);
        if (status is not ("processing" or "requires_review")) throw InvalidState(analysisId, status, "store extraction results");
    }

    private static async Task EnsureReviewableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select status from analyses where id = $1 for update";
        command.Parameters.AddWithValue(analysisId);
        var status = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (status is null) throw NotFound(analysisId);
        if (status != "requires_review") throw InvalidState(analysisId, status, "reviewed");
    }

    private static async Task ExecuteReviewAcceptanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        Guid ownerUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var requirements = connection.CreateCommand())
        {
            requirements.Transaction = transaction;
            requirements.CommandText = """
                update analysis_requirements
                set review_status = 'accepted', reviewed_by_user_id = $1, reviewed_at = $2,
                    updated_at = $2, version = version + 1
                where analysis_id = $3 and review_status = 'pending'
                """;
            requirements.Parameters.AddWithValue(ownerUserId);
            requirements.Parameters.AddWithValue(now);
            requirements.Parameters.AddWithValue(analysisId);
            await requirements.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var findings = connection.CreateCommand();
        findings.Transaction = transaction;
        findings.CommandText = """
            update analysis_findings
            set review_status = 'accepted', reviewed_by_user_id = $1, reviewed_at = $2,
                updated_at = $2, version = version + 1
            where analysis_id = $3 and review_status = 'pending'
            """;
        findings.Parameters.AddWithValue(ownerUserId);
        findings.Parameters.AddWithValue(now);
        findings.Parameters.AddWithValue(analysisId);
        await findings.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RefreshCorrectionCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            update analyses
            set correction_count =
                (select count(*) from analysis_requirements where analysis_id = $1 and review_status = 'corrected') +
                (select count(*) from analysis_findings where analysis_id = $1 and review_status = 'corrected'),
                updated_at = $2,
                version = version + 1
            where id = $1
            """;
        command.Parameters.AddWithValue(analysisId);
        command.Parameters.AddWithValue(now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(analysisId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetTenantContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select set_config('app.organization_id', $1, true)";
        command.Parameters.AddWithValue(organizationId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertExtractionEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ExtractAnalysisCommand command,
        string extractionStatus,
        int pageCount,
        int requirementCount,
        int findingCount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.CreateVersion7();
        await InsertEventAsync(
            connection,
            transaction,
            eventId,
            "analysis.extraction_completed.v2",
            command.AnalysisId,
            JsonSerializer.Serialize(new
            {
                eventId,
                eventType = "analysis.extraction_completed.v2",
                occurredAt,
                correlationId = command.CorrelationId,
                organizationId = command.OrganizationId,
                actor = new { type = "service", id = "analysis-intake-workflow" },
                payload = new { analysisId = command.AnalysisId, extractionStatus, pageCount, requirementCount, findingCount },
            }, JsonOptions),
            occurredAt,
            cancellationToken);
    }

    private static async Task InsertPublicationEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PublishAnalysisCommand command,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.CreateVersion7();
        await InsertEventAsync(
            connection,
            transaction,
            eventId,
            "analysis.published.v1",
            command.AnalysisId,
            JsonSerializer.Serialize(new
            {
                eventId,
                eventType = "analysis.published.v1",
                occurredAt,
                correlationId = command.CorrelationId,
                organizationId = command.OrganizationId,
                actor = new { type = "user", id = command.OwnerUserId },
                payload = new { analysisId = command.AnalysisId },
            }, JsonOptions),
            occurredAt,
            cancellationToken);
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid eventId,
        string eventType,
        Guid analysisId,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            insert into outbox_events (
                id, event_type, aggregate_type, aggregate_id, payload, occurred_at, available_at
            ) values ($1, $2, 'analysis', $3, $4::jsonb, $5, $5)
            """;
        insert.Parameters.AddWithValue(eventId);
        insert.Parameters.AddWithValue(eventType);
        insert.Parameters.AddWithValue(analysisId);
        insert.Parameters.AddWithValue(payload);
        insert.Parameters.AddWithValue(occurredAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteReviewAuditAsync(
        Guid organizationId,
        Guid ownerUserId,
        Guid analysisId,
        string action,
        string summary,
        string correlationId,
        CancellationToken cancellationToken) => await auditWriter.AppendAsync(
            new AuditEventWrite(
                "user", ownerUserId.ToString(), action, "analysis", analysisId.ToString(),
                organizationId, correlationId, null, summary, "{}", timeProvider.GetUtcNow()),
            cancellationToken);

    private static void ValidateReview(string detail, string titleOrCategory, string status, string? correctionNote)
    {
        if (string.IsNullOrWhiteSpace(detail) || detail.Trim().Length > 5_000 ||
            string.IsNullOrWhiteSpace(titleOrCategory) || titleOrCategory.Trim().Length > 200 ||
            status is not ("accepted" or "corrected" or "rejected") ||
            (status == "corrected" && string.IsNullOrWhiteSpace(correctionNote)) ||
            correctionNote?.Trim().Length > 2_000)
        {
            throw InvalidRequest("The review update is invalid or incomplete.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AnalysisOperationException NotFound(Guid analysisId) => new(
        "analysis_not_found", $"Analysis {analysisId} was not found.", StatusCodes.Status404NotFound);

    private static AnalysisOperationException InvalidState(Guid analysisId, string status, string action) => new(
        "analysis_review_state_invalid",
        $"Analysis {analysisId} cannot be {action} while its status is {status}.",
        StatusCodes.Status409Conflict);

    private static AnalysisOperationException VersionConflict(string target) => new(
        "review_version_conflict", $"The {target} changed before this review update.", StatusCodes.Status409Conflict);

    private static AnalysisOperationException InvalidRequest(string message) => new(
        "invalid_analysis_review", message, StatusCodes.Status400BadRequest);

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private sealed record SourceFile(
        Guid Id,
        string OriginalFileName,
        string Sha256,
        string StorageBucket,
        string StorageKey);

    private sealed record SourceState(
        string AnalysisStatus,
        string ExtractionStatus,
        string? ExtractionVersion,
        IReadOnlyList<SourceFile> Files);

    private sealed record HeaderRow(
        string AnalysisStatus,
        string ExtractionStatus,
        string? ExtractionVersion,
        DateTimeOffset? ExtractionCompletedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? ReviewedAt,
        DateTimeOffset? PublishedAt,
        string? ReviewNote,
        int CorrectionCount);

    private sealed record ProcessedFile(
        SourceFile Source,
        string ExtractionStatus,
        string? DocumentType,
        IReadOnlyList<RequirementSourcePage> Pages,
        string? FailureCode)
    {
        public static ProcessedFile Failed(SourceFile source, string failureCode) =>
            new(source, "failed", null, [], failureCode);
    }

    private sealed record RequirementRow(
        Guid Id,
        string? RequirementCode,
        string RequirementText,
        string OriginalRequirementText,
        string NormalizedRequirement,
        string Category,
        bool Mandatory,
        string? RequestedEvidence,
        decimal Confidence,
        string ReviewStatus,
        string? CorrectionNote,
        int Version);
}
