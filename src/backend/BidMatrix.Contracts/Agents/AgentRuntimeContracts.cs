using System.Text.Json;

namespace BidMatrix.Contracts.Agents;

public sealed record CreateAgentDemoRequest(JsonElement? Input);

public sealed record AgentDemoTaskResponse(
    string TaskId,
    string OrganizationId,
    string AgentKey,
    string Status,
    string WorkflowId,
    DateTimeOffset CreatedAt);

public sealed record PrepareAgentTaskRequest(
    string OrganizationId,
    string WorkflowId,
    string CorrelationId,
    string RuntimeMode,
    string ModelName);

public sealed record AgentTaskPreparationResponse(
    string TaskId,
    string OrganizationId,
    string AgentRunId,
    string AgentKey,
    int AgentVersion,
    string ModelName,
    string PromptVersion,
    IReadOnlyList<string> AllowedTools,
    JsonElement Input,
    string WorkflowId,
    string CorrelationId);

public sealed record CompleteAgentRunRequest(
    string OrganizationId,
    string AgentRunId,
    string OutputArtifactId,
    JsonElement Output,
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    long? ReasoningTokens,
    string CorrelationId);

public sealed record FailAgentRunRequest(
    string OrganizationId,
    string? AgentRunId,
    string FailureCode,
    string FailureMessage,
    string CorrelationId);

public sealed record AgentRunResponse(
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

public sealed record AgentRunListResponse(IReadOnlyList<AgentRunResponse> Runs);
