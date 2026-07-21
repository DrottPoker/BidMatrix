namespace BidMatrix.Application.Workflows;

public sealed record ClaimedOutboxEvent(
    Guid EventId,
    string EventType,
    Guid AggregateId,
    string Payload,
    int AttemptCount,
    DateTimeOffset LeaseExpiresAt);

public sealed record AnalysisIntakeState(
    Guid AnalysisId,
    Guid OrganizationId,
    string Status,
    IReadOnlyList<string> FileScanStatuses);

public interface IWorkflowBridgeService
{
    Task<IReadOnlyList<ClaimedOutboxEvent>> ClaimAsync(
        string workerId,
        string eventType,
        int limit,
        CancellationToken cancellationToken = default);

    Task<bool> AcknowledgeAsync(
        Guid eventId,
        string workerId,
        CancellationToken cancellationToken = default);

    Task<bool> FailAsync(
        Guid eventId,
        string workerId,
        string error,
        CancellationToken cancellationToken = default);

    Task<AnalysisIntakeState?> GetAnalysisIntakeAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default);

    Task MarkAnalysisProcessingAsync(
        Guid organizationId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateManualReviewTaskAsync(
        Guid organizationId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task MarkAnalysisRequiresReviewAsync(
        Guid organizationId,
        Guid analysisId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
