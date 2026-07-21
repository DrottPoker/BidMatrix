using System.Text.Json;

namespace BidMatrix.Contracts.Tools;

public sealed record ToolRequestContext(string OrganizationId, string CorrelationId);

public sealed record ToolGatewayRequestContract(
    string RequestId,
    string TaskId,
    string AgentRunId,
    string AgentKey,
    string ToolKey,
    string IdempotencyKey,
    JsonElement Arguments,
    ToolRequestContext Context);

public sealed record ToolGatewayResponse(
    string Decision,
    string ToolCallId,
    string PolicyVersion,
    string? ApprovalId,
    string ReasonCode,
    string InputHash,
    string ExecutionStatus,
    JsonElement? Output);

public sealed record ApprovalDecisionRequest(
    string Decision,
    JsonElement Payload,
    int ExpectedVersion,
    string? Note);

public sealed record OwnerApprovalActionRequest(
    JsonElement Payload,
    int ExpectedVersion,
    string? Note);

public sealed record ApprovalResponse(
    string Id,
    string OrganizationId,
    string? ToolCallId,
    string? TaskId,
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
    string? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    string? ExecutionStatus,
    int Version,
    string? SupersedesApprovalId);

public sealed record ApprovalListResponse(IReadOnlyList<ApprovalResponse> Approvals);
