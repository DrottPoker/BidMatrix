using System.Security.Cryptography;
using System.Text.Json;
using BidMatrix.Application.Analyses;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace BidMatrix.Infrastructure.Analyses;

public sealed class PostgresAnalysisService(
    NpgsqlDataSource dataSource,
    IObjectStorage objectStorage,
    IFileScanner fileScanner,
    AnalysisOptions analysisOptions,
    S3StorageOptions storageOptions,
    TimeProvider timeProvider) : IAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;
    private static ReadOnlySpan<byte> PdfEndMarker => "%%EOF"u8;
    private static ReadOnlySpan<byte> PdfEncryptMarker => "/Encrypt"u8;

    public async Task<AnalysisRecord> CreateAsync(
        CreateAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 200)
        {
            throw new AnalysisOperationException(
                "invalid_idempotency_key",
                "Idempotency-Key must contain between 1 and 200 characters.",
                StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow();
        var analysisId = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);

        var existingId = await FindAnalysisByIdempotencyKeyAsync(
            connection,
            transaction,
            command.OrganizationId,
            command.IdempotencyKey,
            cancellationToken);

        if (existingId is not null)
        {
            var existing = await LoadAnalysisAsync(
                connection,
                transaction,
                command.OrganizationId,
                existingId.Value,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing ?? throw new InvalidOperationException("Idempotent analysis record disappeared.");
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into analyses (
                    id,
                    organization_id,
                    title,
                    status,
                    source_language,
                    created_by_user_id,
                    requires_human_review,
                    created_at,
                    updated_at,
                    version,
                    idempotency_key
                )
                values ($1, $2, $3, 'draft', 'en', $4, true, $5, $5, 1, $6)
                """;
            insert.Parameters.AddWithValue(analysisId);
            insert.Parameters.AddWithValue(command.OrganizationId);
            insert.Parameters.AddWithValue((object?)NormalizeTitle(command.Title) ?? DBNull.Value);
            insert.Parameters.AddWithValue(command.UserId);
            insert.Parameters.AddWithValue(now);
            insert.Parameters.AddWithValue(command.IdempotencyKey);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertOutboxEventAsync(
            connection,
            transaction,
            "analysis.created.v1",
            analysisId,
            command.OrganizationId,
            command.UserId,
            command.CorrelationId,
            new { analysisId, title = NormalizeTitle(command.Title) },
            now,
            cancellationToken);

        var created = await LoadAnalysisAsync(
            connection,
            transaction,
            command.OrganizationId,
            analysisId,
            cancellationToken)
            ?? throw new InvalidOperationException("Created analysis could not be loaded.");
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<IReadOnlyList<AnalysisRecord>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        var ids = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "select id from analyses order by created_at desc";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        var analyses = new List<AnalysisRecord>(ids.Count);
        foreach (var id in ids)
        {
            var analysis = await LoadAnalysisAsync(connection, transaction, organizationId, id, cancellationToken);
            if (analysis is not null)
            {
                analyses.Add(analysis);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return analyses;
    }

    public async Task<AnalysisRecord?> GetAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);
        var analysis = await LoadAnalysisAsync(connection, transaction, organizationId, analysisId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return analysis;
    }

    public async Task<AnalysisFileUploadResult> UploadFileAsync(
        UploadAnalysisFileCommand command,
        CancellationToken cancellationToken = default)
    {
        var originalFileName = ValidatePdf(command.OriginalFileName, command.ContentType, command.Content);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(command.Content));
        var now = timeProvider.GetUtcNow();
        var fileId = Guid.CreateVersion7();
        var storageKey = $"organizations/{command.OrganizationId}/analyses/{command.AnalysisId}/files/{fileId}/original.pdf";
        var retentionUntil = now.AddDays(analysisOptions.FileRetentionDays);

        var registration = await RegisterPendingFileAsync(
            command,
            originalFileName,
            sha256,
            fileId,
            storageKey,
            retentionUntil,
            now,
            cancellationToken);

        if (registration is not null)
        {
            return new AnalysisFileUploadResult(registration, true);
        }

        FileScanResult scanResult;
        try
        {
            await objectStorage.PutAsync(
                new ObjectWriteRequest(
                    storageOptions.QuarantineBucket,
                    storageKey,
                    "application/pdf",
                    sha256,
                    command.Content),
                cancellationToken);
            scanResult = await fileScanner.ScanAsync(command.Content, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MarkUploadFailedAsync(
                command.OrganizationId,
                command.AnalysisId,
                fileId,
                "storage_or_scan_failed",
                cancellationToken);
            throw new AnalysisOperationException(
                "file_storage_failed",
                "The PDF could not be stored in quarantine.",
                StatusCodes.Status502BadGateway);
        }

        var file = await CompleteFileUploadAsync(
            command,
            fileId,
            scanResult.Status,
            now,
            cancellationToken);
        return new AnalysisFileUploadResult(file, false);
    }

    public async Task<AnalysisRecord> SubmitAsync(
        Guid organizationId,
        Guid userId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        var status = await LockAnalysisStatusAsync(connection, transaction, analysisId, cancellationToken);
        if (status is null)
        {
            throw NotFound(analysisId);
        }

        if (status is "queued" or "processing" or "requires_review" or "completed")
        {
            var existing = await LoadAnalysisAsync(connection, transaction, organizationId, analysisId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing ?? throw NotFound(analysisId);
        }

        if (status is "cancelled" or "failed")
        {
            throw new AnalysisOperationException(
                "analysis_not_submittable",
                $"Analysis in status {status} cannot be submitted.",
                StatusCodes.Status409Conflict);
        }

        var fileStatuses = await LoadFileScanStatusesAsync(connection, transaction, analysisId, cancellationToken);
        if (fileStatuses.Count == 0)
        {
            throw new AnalysisOperationException(
                "analysis_has_no_files",
                "At least one PDF is required before submission.",
                StatusCodes.Status409Conflict);
        }

        var allowedStatuses = fileScanner.DevelopmentBypassEnabled
            ? new[] { "clean", "development_bypass" }
            : ["clean"];
        if (fileStatuses.Any(statusValue => !allowedStatuses.Contains(statusValue, StringComparer.Ordinal)))
        {
            throw new AnalysisOperationException(
                "file_scan_incomplete",
                "Every PDF must have an allowed scan status before submission.",
                StatusCodes.Status409Conflict);
        }

        var workflowId = $"analysis-intake-{analysisId}";
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analyses
                set status = 'queued',
                    workflow_id = $1,
                    updated_at = $2,
                    version = version + 1
                where id = $3
                """;
            update.Parameters.AddWithValue(workflowId);
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(analysisId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertOutboxEventAsync(
            connection,
            transaction,
            "analysis.submitted.v1",
            analysisId,
            organizationId,
            userId,
            correlationId,
            new { analysisId, workflowId },
            now,
            cancellationToken);

        var submitted = await LoadAnalysisAsync(connection, transaction, organizationId, analysisId, cancellationToken)
            ?? throw NotFound(analysisId);
        await transaction.CommitAsync(cancellationToken);
        return submitted;
    }

    public async Task<AnalysisRecord> CancelAsync(
        Guid organizationId,
        Guid userId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);

        var status = await LockAnalysisStatusAsync(connection, transaction, analysisId, cancellationToken);
        if (status is null)
        {
            throw NotFound(analysisId);
        }

        if (status == "cancelled")
        {
            var existing = await LoadAnalysisAsync(connection, transaction, organizationId, analysisId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return existing ?? throw NotFound(analysisId);
        }

        if (status == "completed")
        {
            throw new AnalysisOperationException(
                "analysis_already_completed",
                "A completed analysis cannot be cancelled.",
                StatusCodes.Status409Conflict);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analyses
                set status = 'cancelled',
                    updated_at = $1,
                    completed_at = $1,
                    version = version + 1
                where id = $2
                """;
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(analysisId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertOutboxEventAsync(
            connection,
            transaction,
            "analysis.cancelled.v1",
            analysisId,
            organizationId,
            userId,
            correlationId,
            new { analysisId },
            now,
            cancellationToken);

        var cancelled = await LoadAnalysisAsync(connection, transaction, organizationId, analysisId, cancellationToken)
            ?? throw NotFound(analysisId);
        await transaction.CommitAsync(cancellationToken);
        return cancelled;
    }

    private async Task<AnalysisFileRecord?> RegisterPendingFileAsync(
        UploadAnalysisFileCommand command,
        string originalFileName,
        string sha256,
        Guid fileId,
        string storageKey,
        DateTimeOffset retentionUntil,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);

        var status = await LockAnalysisStatusAsync(connection, transaction, command.AnalysisId, cancellationToken);
        if (status is null)
        {
            throw NotFound(command.AnalysisId);
        }

        if (status is not ("draft" or "uploading" or "quarantined"))
        {
            throw new AnalysisOperationException(
                "analysis_not_uploadable",
                $"Files cannot be uploaded while the analysis is {status}.",
                StatusCodes.Status409Conflict);
        }

        var duplicate = await FindFileByHashAsync(
            connection,
            transaction,
            command.AnalysisId,
            sha256,
            cancellationToken);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return duplicate;
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                insert into analysis_files (
                    id,
                    organization_id,
                    analysis_id,
                    original_file_name,
                    content_type,
                    size_bytes,
                    sha256,
                    storage_bucket,
                    storage_key,
                    scan_status,
                    document_type,
                    retention_until,
                    created_at,
                    updated_at
                )
                values ($1, $2, $3, $4, 'application/pdf', $5, $6, $7, $8, 'pending', 'rfp_source', $9, $10, $10)
                """;
            insert.Parameters.AddWithValue(fileId);
            insert.Parameters.AddWithValue(command.OrganizationId);
            insert.Parameters.AddWithValue(command.AnalysisId);
            insert.Parameters.AddWithValue(originalFileName);
            insert.Parameters.AddWithValue(command.Content.LongLength);
            insert.Parameters.AddWithValue(sha256);
            insert.Parameters.AddWithValue(storageOptions.QuarantineBucket);
            insert.Parameters.AddWithValue(storageKey);
            insert.Parameters.AddWithValue(retentionUntil);
            insert.Parameters.AddWithValue(now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                update analyses
                set status = 'uploading',
                    updated_at = $1,
                    version = version + 1
                where id = $2
                """;
            update.Parameters.AddWithValue(now);
            update.Parameters.AddWithValue(command.AnalysisId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private async Task<AnalysisFileRecord> CompleteFileUploadAsync(
        UploadAnalysisFileCommand command,
        Guid fileId,
        string scanStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, command.OrganizationId, cancellationToken);

        await using (var updateFile = connection.CreateCommand())
        {
            updateFile.Transaction = transaction;
            updateFile.CommandText = """
                update analysis_files
                set scan_status = $1,
                    updated_at = $2
                where id = $3 and analysis_id = $4
                """;
            updateFile.Parameters.AddWithValue(scanStatus);
            updateFile.Parameters.AddWithValue(now);
            updateFile.Parameters.AddWithValue(fileId);
            updateFile.Parameters.AddWithValue(command.AnalysisId);
            if (await updateFile.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Pending analysis file could not be completed.");
            }
        }

        await using (var updateAnalysis = connection.CreateCommand())
        {
            updateAnalysis.Transaction = transaction;
            updateAnalysis.CommandText = """
                update analyses
                set status = 'quarantined',
                    updated_at = $1,
                    version = version + 1
                where id = $2
                """;
            updateAnalysis.Parameters.AddWithValue(now);
            updateAnalysis.Parameters.AddWithValue(command.AnalysisId);
            await updateAnalysis.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertOutboxEventAsync(
            connection,
            transaction,
            "analysis.file_uploaded.v1",
            command.AnalysisId,
            command.OrganizationId,
            command.UserId,
            command.CorrelationId,
            new { analysisId = command.AnalysisId, fileId, scanStatus },
            now,
            cancellationToken);

        var file = await LoadFileAsync(connection, transaction, fileId, cancellationToken)
            ?? throw new InvalidOperationException("Completed analysis file could not be loaded.");
        await transaction.CommitAsync(cancellationToken);
        return file;
    }

    private async Task MarkUploadFailedAsync(
        Guid organizationId,
        Guid analysisId,
        Guid fileId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantContextAsync(connection, transaction, organizationId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        await using (var fileCommand = connection.CreateCommand())
        {
            fileCommand.Transaction = transaction;
            fileCommand.CommandText = "update analysis_files set scan_status = 'failed', updated_at = $1 where id = $2";
            fileCommand.Parameters.AddWithValue(now);
            fileCommand.Parameters.AddWithValue(fileId);
            await fileCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var analysisCommand = connection.CreateCommand())
        {
            analysisCommand.Transaction = transaction;
            analysisCommand.CommandText = """
                update analyses
                set status = 'failed',
                    failure_code = $1,
                    failure_message = 'The quarantined file could not be stored or scanned.',
                    updated_at = $2,
                    version = version + 1
                where id = $3
                """;
            analysisCommand.Parameters.AddWithValue(failureCode);
            analysisCommand.Parameters.AddWithValue(now);
            analysisCommand.Parameters.AddWithValue(analysisId);
            await analysisCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private string ValidatePdf(string fileName, string contentType, byte[] content)
    {
        var originalFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(originalFileName) ||
            !string.Equals(Path.GetExtension(originalFileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidPdf("Only files with a .pdf extension are accepted.");
        }

        var mediaType = contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidPdf("The uploaded file must use the application/pdf media type.");
        }

        if (content.Length == 0)
        {
            throw InvalidPdf("Empty PDF files are not accepted.");
        }

        if (content.LongLength > analysisOptions.MaxPdfUploadBytes)
        {
            throw new AnalysisOperationException(
                "file_too_large",
                $"The PDF exceeds the {analysisOptions.MaxPdfUploadBytes}-byte limit.",
                StatusCodes.Status413PayloadTooLarge);
        }

        var bytes = content.AsSpan();
        if (!bytes.StartsWith(PdfSignature))
        {
            throw InvalidPdf("The uploaded file does not have a valid PDF signature.");
        }

        var trailerStart = Math.Max(0, bytes.Length - 2048);
        if (bytes[trailerStart..].LastIndexOf(PdfEndMarker) < 0)
        {
            throw InvalidPdf("The PDF is malformed because its end marker is missing.");
        }

        if (bytes.IndexOf(PdfEncryptMarker) >= 0)
        {
            throw InvalidPdf("Encrypted PDFs are not supported by the current extraction pipeline.");
        }

        return originalFileName;
    }

    private static AnalysisOperationException InvalidPdf(string message) => new(
        "invalid_pdf",
        message,
        StatusCodes.Status400BadRequest);

    private static AnalysisOperationException NotFound(Guid analysisId) => new(
        "analysis_not_found",
        $"Analysis {analysisId} was not found.",
        StatusCodes.Status404NotFound);

    private static string? NormalizeTitle(string? title)
    {
        var normalized = title?.Trim();
        if (normalized?.Length > 200)
        {
            throw new AnalysisOperationException(
                "invalid_title",
                "Analysis title cannot exceed 200 characters.",
                StatusCodes.Status400BadRequest);
        }

        return string.IsNullOrEmpty(normalized) ? null : normalized;
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

    private static async Task<Guid?> FindAnalysisByIdempotencyKeyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select id from analyses where organization_id = $1 and idempotency_key = $2";
        command.Parameters.AddWithValue(organizationId);
        command.Parameters.AddWithValue(idempotencyKey);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    private static async Task<string?> LockAnalysisStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select status from analyses where id = $1 for update";
        command.Parameters.AddWithValue(analysisId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<IReadOnlyList<string>> LoadFileScanStatusesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var statuses = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select scan_status from analysis_files where analysis_id = $1 order by created_at";
        command.Parameters.AddWithValue(analysisId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            statuses.Add(reader.GetString(0));
        }

        return statuses;
    }

    private static async Task<AnalysisFileRecord?> FindFileByHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid analysisId,
        string sha256,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                id,
                original_file_name,
                content_type,
                size_bytes,
                sha256,
                storage_bucket,
                storage_key,
                scan_status,
                retention_until,
                created_at
            from analysis_files
            where analysis_id = $1 and sha256 = $2
            """;
        command.Parameters.AddWithValue(analysisId);
        command.Parameters.AddWithValue(sha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFile(reader) : null;
    }

    private static async Task<AnalysisFileRecord?> LoadFileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            select
                id,
                original_file_name,
                content_type,
                size_bytes,
                sha256,
                storage_bucket,
                storage_key,
                scan_status,
                retention_until,
                created_at
            from analysis_files
            where id = $1
            """;
        command.Parameters.AddWithValue(fileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFile(reader) : null;
    }

    private static async Task<AnalysisRecord?> LoadAnalysisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        AnalysisRecord? analysis;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                select
                    id,
                    organization_id,
                    title,
                    status,
                    source_language,
                    workflow_id,
                    requires_human_review,
                    failure_code,
                    failure_message,
                    created_at,
                    updated_at,
                    version
                from analyses
                where id = $1 and organization_id = $2
                """;
            command.Parameters.AddWithValue(analysisId);
            command.Parameters.AddWithValue(organizationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            analysis = new AnalysisRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.GetInt32(11),
                []);
        }

        var files = new List<AnalysisFileRecord>();
        await using (var fileCommand = connection.CreateCommand())
        {
            fileCommand.Transaction = transaction;
            fileCommand.CommandText = """
                select
                    id,
                    original_file_name,
                    content_type,
                    size_bytes,
                    sha256,
                    storage_bucket,
                    storage_key,
                    scan_status,
                    retention_until,
                    created_at
                from analysis_files
                where analysis_id = $1
                order by created_at
                """;
            fileCommand.Parameters.AddWithValue(analysisId);
            await using var reader = await fileCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                files.Add(ReadFile(reader));
            }
        }

        return analysis with { Files = files };
    }

    private static AnalysisFileRecord ReadFile(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        reader.GetFieldValue<DateTimeOffset>(9));

    private static async Task InsertOutboxEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventType,
        Guid aggregateId,
        Guid organizationId,
        Guid actorId,
        string correlationId,
        object payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var eventId = Guid.CreateVersion7();
        var envelope = JsonSerializer.Serialize(
            new
            {
                eventId,
                eventType,
                occurredAt,
                correlationId,
                causationId = (string?)null,
                organizationId,
                actor = new { type = "user", id = actorId },
                payload,
            },
            JsonOptions);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into outbox_events (
                id,
                event_type,
                aggregate_type,
                aggregate_id,
                payload,
                occurred_at,
                available_at
            )
            values ($1, $2, 'analysis', $3, $4::jsonb, $5, $5)
            """;
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(aggregateId);
        command.Parameters.AddWithValue(envelope);
        command.Parameters.AddWithValue(occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
