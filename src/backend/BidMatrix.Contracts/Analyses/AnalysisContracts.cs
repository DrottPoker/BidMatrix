namespace BidMatrix.Contracts.Analyses;

public sealed record CreateAnalysisRequest(string? Title);

public sealed record AnalysisFileResponse(
    string Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string ScanStatus,
    DateTimeOffset? RetentionUntil,
    DateTimeOffset CreatedAt);

public sealed record AnalysisResponse(
    string Id,
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
    IReadOnlyList<AnalysisFileResponse> Files);

public sealed record AnalysisListResponse(IReadOnlyList<AnalysisResponse> Analyses);

public sealed record AnalysisFileUploadResponse(AnalysisFileResponse File, bool Duplicate);

public sealed record AnalysisRequirementsResponse(
    string AnalysisId,
    string CapabilityStatus,
    IReadOnlyList<object> Requirements,
    string Message);

public sealed record ClaimedEventResponse(
    string EventId,
    string EventType,
    string AggregateId,
    string Payload,
    int AttemptCount,
    DateTimeOffset LeaseExpiresAt);

public sealed record ClaimEventsResponse(IReadOnlyList<ClaimedEventResponse> Events);

public sealed record FailEventRequest(string Error);

public sealed record AnalysisIntakeRequest(string OrganizationId, string CorrelationId);

public sealed record AnalysisIntakeStateResponse(
    string AnalysisId,
    string OrganizationId,
    string Status,
    IReadOnlyList<string> FileScanStatuses);

public sealed record ManualReviewTaskResponse(string TaskId);
