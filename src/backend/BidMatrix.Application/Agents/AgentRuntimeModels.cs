using System.Text.Json;

namespace BidMatrix.Application.Agents;

public sealed record AgentDemoCreateCommand(
    Guid OrganizationId,
    Guid OwnerUserId,
    string AgentKey,
    JsonElement? Input,
    string IdempotencyKey,
    string CorrelationId);

public sealed record AgentDemoTaskRecord(
    Guid TaskId,
    Guid OrganizationId,
    string AgentKey,
    string Status,
    string WorkflowId,
    DateTimeOffset CreatedAt);

public sealed record AgentTaskPreparation(
    Guid TaskId,
    Guid OrganizationId,
    Guid AgentRunId,
    string AgentKey,
    int AgentVersion,
    string ModelName,
    string PromptVersion,
    IReadOnlyList<string> AllowedTools,
    JsonElement Input,
    string WorkflowId,
    string CorrelationId);

public sealed record CompleteAgentRunCommand(
    Guid TaskId,
    Guid OrganizationId,
    Guid AgentRunId,
    Guid OutputArtifactId,
    JsonElement Output,
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    long? ReasoningTokens,
    string CorrelationId);

public sealed record FailAgentRunCommand(
    Guid TaskId,
    Guid OrganizationId,
    Guid? AgentRunId,
    string FailureCode,
    string FailureMessage,
    string CorrelationId);

public sealed record AgentRunRecord(
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

public interface IAgentRuntimeService
{
    Task<AgentDemoTaskRecord> CreateDemoAsync(
        AgentDemoCreateCommand command,
        CancellationToken cancellationToken = default);

    Task<AgentTaskPreparation> PrepareAsync(
        Guid organizationId,
        Guid taskId,
        string workflowId,
        string correlationId,
        string runtimeMode,
        string modelName,
        CancellationToken cancellationToken = default);

    Task<AgentRunRecord> CompleteAsync(
        CompleteAgentRunCommand command,
        CancellationToken cancellationToken = default);

    Task<AgentRunRecord?> FailAsync(
        FailAgentRunCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentRunRecord>> ListRunsAsync(
        Guid? organizationId,
        CancellationToken cancellationToken = default);
}
