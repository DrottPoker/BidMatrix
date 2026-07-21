using System.Text.Json;

namespace BidMatrix.Application.Owner;

public sealed record OwnerDashboardRecord(
    IReadOnlyDictionary<string, int> AnalysesByStatus,
    int OpenTasks,
    int PendingApprovals,
    int WorkflowFailures,
    IReadOnlyList<OwnerAgentRunRecord> RecentAgentRuns,
    IReadOnlyList<OwnerSystemControlRecord> SystemControls,
    bool AuditChainValid,
    DateTimeOffset GeneratedAt);

public sealed record OwnerTaskRecord(
    Guid Id,
    Guid? OrganizationId,
    Guid? GoalId,
    string Type,
    string Title,
    string Description,
    string Priority,
    string Status,
    string? AssignedAgentKey,
    JsonElement Input,
    JsonElement Constraints,
    Guid? ResultArtifactId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record CreateOwnerTaskCommand(
    Guid OrganizationId,
    Guid OwnerUserId,
    Guid? GoalId,
    string Type,
    string Title,
    string Description,
    string Priority,
    string? AssignedAgentKey,
    JsonElement Input,
    JsonElement Constraints,
    string IdempotencyKey,
    string CorrelationId);

public sealed record CancelOwnerTaskCommand(
    Guid OrganizationId,
    Guid OwnerUserId,
    Guid TaskId,
    int ExpectedVersion,
    string? Note,
    string CorrelationId);

public sealed record OwnerAgentDefinitionRecord(
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

public sealed record OwnerAgentRunRecord(
    Guid Id,
    Guid OrganizationId,
    Guid TaskId,
    string AgentKey,
    string Status,
    string ModelName,
    string PromptVersion,
    Guid? OutputArtifactId,
    long InputTokens,
    long OutputTokens,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    string? FailureMessage);

public sealed record OwnerWorkflowRecord(
    Guid Id,
    Guid OrganizationId,
    string WorkflowType,
    string WorkflowId,
    string? TemporalRunId,
    Guid? TaskId,
    string Status,
    JsonElement Input,
    JsonElement? Result,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

public sealed record OwnerArtifactRecord(
    Guid Id,
    Guid? OrganizationId,
    string ArtifactType,
    string Title,
    string ContentType,
    JsonElement? InlineContent,
    string Sha256,
    string Sensitivity,
    string CreatedByType,
    string CreatedById,
    DateTimeOffset CreatedAt,
    Guid? SupersedesArtifactId);

public sealed record OwnerAuditEventRecord(
    Guid Id,
    long SequenceNumber,
    string ActorType,
    string ActorId,
    string Action,
    string? TargetType,
    string? TargetId,
    Guid? OrganizationId,
    string? RequestId,
    string? TraceId,
    string Summary,
    string? PreviousHash,
    string EventHash,
    bool ChainLinkValid,
    DateTimeOffset CreatedAt);

public sealed record OwnerGoalRecord(
    Guid Id,
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

public sealed record CreateOwnerGoalCommand(
    Guid OwnerUserId,
    string Title,
    string Description,
    string? MetricKey,
    decimal? TargetValue,
    DateTimeOffset? TargetDate,
    string Status,
    JsonElement Constraints,
    string CorrelationId);

public sealed record UpdateOwnerGoalCommand(
    Guid OwnerUserId,
    Guid GoalId,
    string Title,
    string Description,
    string? MetricKey,
    decimal? TargetValue,
    DateTimeOffset? TargetDate,
    string Status,
    JsonElement Constraints,
    int ExpectedVersion,
    string CorrelationId);

public sealed record OwnerSystemControlRecord(
    string ControlKey,
    bool Enabled,
    JsonElement Value,
    Guid UpdatedByUserId,
    DateTimeOffset UpdatedAt,
    int Version,
    bool LockedForF0);

public sealed record UpdateSystemControlCommand(
    Guid OwnerUserId,
    string ControlKey,
    bool Enabled,
    int ExpectedVersion,
    string Confirmation,
    string CorrelationId);

public sealed class OwnerConsoleException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public interface IOwnerConsoleService
{
    Task<OwnerDashboardRecord> GetDashboardAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerTaskRecord>> ListTasksAsync(Guid organizationId, string? status, string? type, string? agentKey, string? priority, CancellationToken cancellationToken = default);
    Task<OwnerTaskRecord?> GetTaskAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken = default);
    Task<OwnerTaskRecord> CreateTaskAsync(CreateOwnerTaskCommand command, CancellationToken cancellationToken = default);
    Task<OwnerTaskRecord> CancelTaskAsync(CancelOwnerTaskCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerAgentDefinitionRecord>> ListAgentsAsync(CancellationToken cancellationToken = default);
    Task<OwnerAgentDefinitionRecord?> GetAgentAsync(string agentKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerAgentRunRecord>> ListAgentRunsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OwnerAgentRunRecord?> GetAgentRunAsync(Guid organizationId, Guid runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerWorkflowRecord>> ListWorkflowsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OwnerWorkflowRecord?> GetWorkflowAsync(Guid organizationId, Guid workflowRunId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerArtifactRecord>> ListArtifactsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OwnerArtifactRecord?> GetArtifactAsync(Guid organizationId, Guid artifactId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerAuditEventRecord>> ListAuditAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerGoalRecord>> ListGoalsAsync(CancellationToken cancellationToken = default);
    Task<OwnerGoalRecord> CreateGoalAsync(CreateOwnerGoalCommand command, CancellationToken cancellationToken = default);
    Task<OwnerGoalRecord> UpdateGoalAsync(UpdateOwnerGoalCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OwnerSystemControlRecord>> ListSystemControlsAsync(CancellationToken cancellationToken = default);
    Task<OwnerSystemControlRecord> UpdateSystemControlAsync(UpdateSystemControlCommand command, CancellationToken cancellationToken = default);
}
