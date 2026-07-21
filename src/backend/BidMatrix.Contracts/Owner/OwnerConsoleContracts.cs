using System.Text.Json;

namespace BidMatrix.Contracts.Owner;

public sealed record OwnerDashboardResponse(
    IReadOnlyDictionary<string, int> AnalysesByStatus,
    int OpenTasks,
    int PendingApprovals,
    int WorkflowFailures,
    IReadOnlyList<OwnerAgentRunResponse> RecentAgentRuns,
    IReadOnlyList<OwnerSystemControlResponse> SystemControls,
    bool AuditChainValid,
    bool DraftOnly,
    bool ExternalActionsDisabled,
    DateTimeOffset GeneratedAt);

public sealed record OwnerTaskResponse(
    string Id,
    string? OrganizationId,
    string? GoalId,
    string Type,
    string Title,
    string Description,
    string Priority,
    string Status,
    string? AssignedAgentKey,
    JsonElement Input,
    JsonElement Constraints,
    string? ResultArtifactId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record OwnerTaskListResponse(IReadOnlyList<OwnerTaskResponse> Tasks);

public sealed record CreateOwnerTaskRequest(
    string Type,
    string Title,
    string Description,
    string Priority,
    string? AssignedAgentKey,
    string? GoalId,
    JsonElement Input,
    JsonElement Constraints);

public sealed record CancelOwnerTaskRequest(int ExpectedVersion, string? Note);

public sealed record OwnerAgentResponse(
    string AgentKey,
    string DisplayName,
    string Description,
    string Status,
    int Version,
    string PromptVersion,
    string ModelKey,
    IReadOnlyList<string> ToolPermissions,
    int TotalRuns,
    int FailedRuns,
    long InputTokens,
    long OutputTokens,
    DateTimeOffset? LastRunAt);

public sealed record OwnerAgentListResponse(IReadOnlyList<OwnerAgentResponse> Agents);

public sealed record OwnerAgentRunResponse(
    string Id,
    string OrganizationId,
    string TaskId,
    string AgentKey,
    string Status,
    string ModelName,
    string PromptVersion,
    string? OutputArtifactId,
    long InputTokens,
    long OutputTokens,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    string? FailureMessage);

public sealed record OwnerAgentRunListResponse(IReadOnlyList<OwnerAgentRunResponse> Runs);

public sealed record OwnerWorkflowResponse(
    string Id,
    string OrganizationId,
    string WorkflowType,
    string WorkflowId,
    string? TemporalRunId,
    string? TaskId,
    string Status,
    JsonElement Input,
    JsonElement? Result,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

public sealed record OwnerWorkflowListResponse(IReadOnlyList<OwnerWorkflowResponse> Workflows);

public sealed record OwnerArtifactResponse(
    string Id,
    string? OrganizationId,
    string ArtifactType,
    string Title,
    string ContentType,
    JsonElement? InlineContent,
    string Sha256,
    string Sensitivity,
    string CreatedByType,
    string CreatedById,
    DateTimeOffset CreatedAt,
    string? SupersedesArtifactId);

public sealed record OwnerArtifactListResponse(IReadOnlyList<OwnerArtifactResponse> Artifacts);

public sealed record OwnerAuditEventResponse(
    string Id,
    long SequenceNumber,
    string ActorType,
    string ActorId,
    string Action,
    string? TargetType,
    string? TargetId,
    string? OrganizationId,
    string? RequestId,
    string? TraceId,
    string Summary,
    string? PreviousHash,
    string EventHash,
    bool ChainLinkValid,
    DateTimeOffset CreatedAt);

public sealed record OwnerAuditListResponse(bool ChainValid, IReadOnlyList<OwnerAuditEventResponse> Events);

public sealed record OwnerGoalResponse(
    string Id,
    string Title,
    string Description,
    string? MetricKey,
    decimal? TargetValue,
    DateTimeOffset? TargetDate,
    string Status,
    JsonElement Constraints,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record OwnerGoalListResponse(IReadOnlyList<OwnerGoalResponse> Goals);

public sealed record CreateOwnerGoalRequest(
    string Title,
    string Description,
    string? MetricKey,
    decimal? TargetValue,
    DateTimeOffset? TargetDate,
    string Status,
    JsonElement Constraints);

public sealed record UpdateOwnerGoalRequest(
    string Title,
    string Description,
    string? MetricKey,
    decimal? TargetValue,
    DateTimeOffset? TargetDate,
    string Status,
    JsonElement Constraints,
    int ExpectedVersion);

public sealed record OwnerSystemControlResponse(
    string ControlKey,
    bool Enabled,
    JsonElement Value,
    string UpdatedByUserId,
    DateTimeOffset UpdatedAt,
    int Version,
    bool LockedForF0);

public sealed record OwnerSystemControlListResponse(IReadOnlyList<OwnerSystemControlResponse> Controls);

public sealed record UpdateSystemControlRequest(bool Enabled, int ExpectedVersion, string Confirmation);
