using System.Text.Json;

namespace BidMatrix.Application.Tools;

public static class ApprovalDecisions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string EditAndCreateRevision = "editAndCreateRevision";
    public const string Cancel = "cancel";
}

public sealed record ApprovalRecord(
    Guid Id,
    Guid OrganizationId,
    Guid? ToolCallId,
    Guid? TaskId,
    string ActionType,
    string Status,
    string Summary,
    JsonElement NormalizedPayload,
    string PayloadHash,
    string PolicyVersion,
    string RiskLevel,
    bool TechnicallyEnabled,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    string? ExecutionStatus,
    int Version,
    Guid? SupersedesApprovalId);

public sealed record ApprovalDecisionCommand(
    Guid ApprovalId,
    Guid OrganizationId,
    Guid OwnerUserId,
    string Decision,
    JsonElement Payload,
    int ExpectedVersion,
    string? Note,
    string CorrelationId);

public interface IApprovalService
{
    Task<IReadOnlyList<ApprovalRecord>> ListAsync(
        Guid? organizationId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<ApprovalRecord?> GetAsync(
        Guid approvalId,
        Guid? organizationId,
        CancellationToken cancellationToken = default);

    Task<ApprovalRecord> DecideAsync(
        ApprovalDecisionCommand command,
        CancellationToken cancellationToken = default);

    Task<ApprovalRecord?> ExpireAsync(
        Guid approvalId,
        CancellationToken cancellationToken = default);
}
