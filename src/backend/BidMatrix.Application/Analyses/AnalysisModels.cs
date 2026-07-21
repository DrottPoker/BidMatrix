namespace BidMatrix.Application.Analyses;

public sealed record AnalysisFileRecord(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string StorageBucket,
    string StorageKey,
    string ScanStatus,
    DateTimeOffset? RetentionUntil,
    DateTimeOffset CreatedAt);

public sealed record AnalysisRecord(
    Guid Id,
    Guid OrganizationId,
    string? Title,
    string Status,
    string SourceLanguage,
    string? WorkflowId,
    bool RequiresHumanReview,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    IReadOnlyList<AnalysisFileRecord> Files);

public sealed record CreateAnalysisCommand(
    Guid OrganizationId,
    Guid UserId,
    string? Title,
    string IdempotencyKey,
    string CorrelationId);

public sealed record UploadAnalysisFileCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid AnalysisId,
    string OriginalFileName,
    string ContentType,
    byte[] Content,
    string CorrelationId);

public sealed record AnalysisFileUploadResult(AnalysisFileRecord File, bool Duplicate);

public interface IAnalysisService
{
    Task<AnalysisRecord> CreateAsync(CreateAnalysisCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnalysisRecord>> ListAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<AnalysisRecord?> GetAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default);

    Task<AnalysisFileUploadResult> UploadFileAsync(
        UploadAnalysisFileCommand command,
        CancellationToken cancellationToken = default);

    Task<AnalysisRecord> SubmitAsync(
        Guid organizationId,
        Guid userId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<AnalysisRecord> CancelAsync(
        Guid organizationId,
        Guid userId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class AnalysisOperationException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
