using System.Security.Cryptography;
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
            throw new AnalysisOperationException(
                "analysis_extraction_state_invalid",
                $"Analysis {command.AnalysisId} cannot be extracted while its status is {source.AnalysisStatus}.",
                StatusCodes.Status409Conflict);
        }

        var extractionVersion = $"{textExtractor.Version}+{requirementDetector.Version}";
        if (source.ExtractionStatus is "succeeded" or "partial" &&
            string.Equals(source.ExtractionVersion, extractionVersion, StringComparison.Ordinal))
        {
            return await GetAsync(command.OrganizationId, command.AnalysisId, cancellationToken)
                ?? throw NotFound(command.AnalysisId);
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

        var detectedRequirements = requirementDetector.Detect(allPages);
        await PersistAsync(
            command,
            extractionVersion,
            processedFiles,
            detectedRequirements,
            cancellationToken);
        return await GetAsync(command.OrganizationId, command.AnalysisId, cancellationToken)
            ?? throw NotFound(command.AnalysisId);
    }

    public async Task<AnalysisExtractionSnapshot?> GetAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        string extractionStatus;
        string? extractionVersion;
        DateTimeOffset? completedAt;
        await using (var header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText = "select extraction_status, extraction_version, extraction_completed_at from analyses where id = $1";
            header.Parameters.AddWithValue(analysisId);
            await using var reader = await header.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            extractionStatus = reader.GetString(0);
            extractionVersion = reader.IsDBNull(1) ? null : reader.GetString(1);
            completedAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
        }

        var documents = await LoadDocumentsAsync(connection, transaction, analysisId, cancellationToken);
        var requirements = await LoadRequirementsAsync(connection, transaction, analysisId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var metrics = new AnalysisExtractionMetrics(
            documents.Count,
            documents.Sum(document => document.PageCount ?? 0),
            requirements.Count,
            requirements.Count(requirement => requirement.Mandatory),
            requirements.Count(requirement => requirement.Citations.Count > 0),
            documents.Count(document => document.ExtractionStatus == "requires_ocr"),
            documents.Count(document => document.ExtractionStatus == "failed"));
        return new AnalysisExtractionSnapshot(
            analysisId,
            extractionStatus,
            extractionVersion,
            completedAt,
            documents,
            requirements,
            metrics);
    }

    private async Task PersistAsync(
        ExtractAnalysisCommand command,
        string extractionVersion,
        IReadOnlyList<ProcessedFile> processedFiles,
        IReadOnlyList<DetectedRequirement> detectedRequirements,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var extractionStatus = processedFiles.All(file => file.ExtractionStatus == "extracted")
            ? "succeeded"
            : "partial";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);

        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "select status from analyses where id = $1 for update";
            lockCommand.Parameters.AddWithValue(command.AnalysisId);
            var analysisStatus = await lockCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (analysisStatus is null)
            {
                throw NotFound(command.AnalysisId);
            }

            if (analysisStatus is not ("processing" or "requires_review"))
            {
                throw new AnalysisOperationException(
                    "analysis_extraction_state_invalid",
                    $"Analysis {command.AnalysisId} cannot store extraction results while its status is {analysisStatus}.",
                    StatusCodes.Status409Conflict);
            }
        }

        await ExecuteAsync(connection, transaction, "delete from analysis_citations where analysis_id = $1", command.AnalysisId, cancellationToken);
        await ExecuteAsync(connection, transaction, "delete from analysis_requirements where analysis_id = $1", command.AnalysisId, cancellationToken);
        await ExecuteAsync(connection, transaction, "delete from analysis_pages where analysis_id = $1", command.AnalysisId, cancellationToken);

        foreach (var processedFile in processedFiles)
        {
            foreach (var page in processedFile.Pages)
            {
                await using var insertPage = connection.CreateCommand();
                insertPage.Transaction = transaction;
                insertPage.CommandText = """
                    insert into analysis_pages (
                        id, organization_id, analysis_id, analysis_file_id, page_number,
                        text_content, text_sha256, extraction_method, created_at
                    )
                    values ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                    """;
                insertPage.Parameters.AddWithValue(Guid.CreateVersion7());
                insertPage.Parameters.AddWithValue(command.OrganizationId);
                insertPage.Parameters.AddWithValue(command.AnalysisId);
                insertPage.Parameters.AddWithValue(processedFile.Source.Id);
                insertPage.Parameters.AddWithValue(page.PageNumber);
                insertPage.Parameters.AddWithValue(page.Text);
                insertPage.Parameters.AddWithValue(Convert.ToHexStringLower(
                    SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(page.Text))));
                insertPage.Parameters.AddWithValue(textExtractor.Version);
                insertPage.Parameters.AddWithValue(now);
                await insertPage.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var updateFile = connection.CreateCommand();
            updateFile.Transaction = transaction;
            updateFile.CommandText = """
                update analysis_files
                set extraction_status = $1,
                    document_type = $2,
                    page_count = $3,
                    extraction_method = $4,
                    extraction_failure_code = $5,
                    extracted_at = $6,
                    updated_at = $6
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

        foreach (var group in detectedRequirements.GroupBy(
                     requirement => requirement.NormalizedRequirement,
                     StringComparer.Ordinal))
        {
            var representative = group.OrderByDescending(requirement => requirement.Confidence).First();
            var requirementId = Guid.CreateVersion7();
            await using (var insertRequirement = connection.CreateCommand())
            {
                insertRequirement.Transaction = transaction;
                insertRequirement.CommandText = """
                    insert into analysis_requirements (
                        id, organization_id, analysis_id, requirement_code, requirement_text,
                        normalized_requirement, category, mandatory, requested_evidence,
                        confidence, review_status, created_at, updated_at
                    )
                    values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, 'pending', $11, $11)
                    """;
                insertRequirement.Parameters.AddWithValue(requirementId);
                insertRequirement.Parameters.AddWithValue(command.OrganizationId);
                insertRequirement.Parameters.AddWithValue(command.AnalysisId);
                insertRequirement.Parameters.AddWithValue((object?)representative.RequirementCode ?? DBNull.Value);
                insertRequirement.Parameters.AddWithValue(representative.RequirementText);
                insertRequirement.Parameters.AddWithValue(representative.NormalizedRequirement);
                insertRequirement.Parameters.AddWithValue(representative.Category);
                insertRequirement.Parameters.AddWithValue(group.Any(requirement => requirement.Mandatory));
                insertRequirement.Parameters.AddWithValue((object?)representative.RequestedEvidence ?? DBNull.Value);
                insertRequirement.Parameters.AddWithValue(group.Max(requirement => requirement.Confidence));
                insertRequirement.Parameters.AddWithValue(now);
                await insertRequirement.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var citation in group.DistinctBy(
                         item => (item.AnalysisFileId, item.PageNumber, item.QuoteText)))
            {
                await using var insertCitation = connection.CreateCommand();
                insertCitation.Transaction = transaction;
                insertCitation.CommandText = """
                    insert into analysis_citations (
                        id, organization_id, analysis_id, requirement_id, analysis_file_id,
                        page_number, section_text, quote_text, bounding_data, created_at
                    )
                    values ($1, $2, $3, $4, $5, $6, $7, $8, null, $9)
                    """;
                insertCitation.Parameters.AddWithValue(Guid.CreateVersion7());
                insertCitation.Parameters.AddWithValue(command.OrganizationId);
                insertCitation.Parameters.AddWithValue(command.AnalysisId);
                insertCitation.Parameters.AddWithValue(requirementId);
                insertCitation.Parameters.AddWithValue(citation.AnalysisFileId);
                insertCitation.Parameters.AddWithValue(citation.PageNumber);
                insertCitation.Parameters.AddWithValue((object?)citation.SectionText ?? DBNull.Value);
                insertCitation.Parameters.AddWithValue(citation.QuoteText);
                insertCitation.Parameters.AddWithValue(now);
                await insertCitation.ExecuteNonQueryAsync(cancellationToken);
            }
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

        await InsertOutboxEventAsync(
            connection,
            transaction,
            command,
            extractionStatus,
            processedFiles.Sum(file => file.Pages.Count),
            detectedRequirements.Select(requirement => requirement.NormalizedRequirement).Distinct().Count(),
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
                "F1 digital PDF extraction completed and remains pending human review.",
                JsonSerializer.Serialize(new
                {
                    extractionStatus,
                    documentCount = processedFiles.Count,
                    pageCount = processedFiles.Sum(file => file.Pages.Count),
                    requirementCount = detectedRequirements.Select(requirement => requirement.NormalizedRequirement).Distinct().Count(),
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
            extractionVersion = reader.IsDBNull(2) ? null : reader.GetString(2);
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
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
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
                select id, requirement_code, requirement_text, normalized_requirement, category,
                    mandatory, requested_evidence, confidence, review_status
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
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    !reader.IsDBNull(5) && reader.GetBoolean(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                    reader.GetString(8)));
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
                if (!citations.TryGetValue(requirementId, out var requirementCitations))
                {
                    requirementCitations = [];
                    citations[requirementId] = requirementCitations;
                }

                requirementCitations.Add(new AnalysisCitationRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        return requirements.Select(requirement => new AnalysisRequirementRecord(
            requirement.Id,
            requirement.RequirementCode,
            requirement.RequirementText,
            requirement.NormalizedRequirement,
            requirement.Category,
            requirement.Mandatory,
            requirement.RequestedEvidence,
            requirement.Confidence,
            requirement.ReviewStatus,
            citations.GetValueOrDefault(requirement.Id) ?? [])).ToArray();
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

    private static async Task InsertOutboxEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ExtractAnalysisCommand command,
        string extractionStatus,
        int pageCount,
        int requirementCount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.CreateVersion7();
        var eventType = "analysis.extraction_completed.v1";
        var envelope = JsonSerializer.Serialize(new
        {
            eventId,
            eventType,
            occurredAt,
            correlationId = command.CorrelationId,
            causationId = (string?)null,
            organizationId = command.OrganizationId,
            actor = new { type = "service", id = "analysis-intake-workflow" },
            payload = new { analysisId = command.AnalysisId, extractionStatus, pageCount, requirementCount },
        }, JsonOptions);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            insert into outbox_events (
                id, event_type, aggregate_type, aggregate_id, payload, occurred_at, available_at
            )
            values ($1, $2, 'analysis', $3, $4::jsonb, $5, $5)
            """;
        insert.Parameters.AddWithValue(eventId);
        insert.Parameters.AddWithValue(eventType);
        insert.Parameters.AddWithValue(command.AnalysisId);
        insert.Parameters.AddWithValue(envelope);
        insert.Parameters.AddWithValue(occurredAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AnalysisOperationException NotFound(Guid analysisId) => new(
        "analysis_not_found",
        $"Analysis {analysisId} was not found.",
        StatusCodes.Status404NotFound);

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
        string NormalizedRequirement,
        string Category,
        bool Mandatory,
        string? RequestedEvidence,
        decimal Confidence,
        string ReviewStatus);
}
