using System.Text.Json;
using BidMatrix.Contracts.Agents;
using BidMatrix.Contracts.Owner;

namespace BidMatrix.Contracts.Internal;

public sealed record UpdateInternalTaskStatusRequest(
    string OrganizationId,
    string Status,
    int ExpectedVersion,
    string? ErrorCode,
    string? ErrorMessage,
    string CorrelationId);

public sealed record CreateInternalAgentRunRequest(
    string TaskId,
    string OrganizationId,
    string WorkflowId,
    string CorrelationId,
    string RuntimeMode,
    string ModelName);

public sealed record UpdateInternalAgentRunRequest(
    string TaskId,
    string OrganizationId,
    string Status,
    string? OutputArtifactId,
    JsonElement? Output,
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    long? ReasoningTokens,
    string? FailureCode,
    string? FailureMessage,
    string CorrelationId);

public sealed record InternalKnowledgeSearchRequest(string Query);

public sealed record InternalToolCatalogResponse(IReadOnlyList<InternalToolResponse> Tools);

public sealed record InternalToolResponse(
    string ToolKey,
    string DisplayName,
    string Description,
    string RiskLevel,
    string SideEffectClass,
    bool Enabled,
    string ApprovalMode);

public sealed record InternalToolEvaluationRequest(
    string OrganizationId,
    string TaskId,
    string AgentRunId,
    string AgentKey,
    string ToolKey);

public sealed record InternalToolEvaluationResponse(
    string Decision,
    string ReasonCode,
    bool TechnicallyEnabled,
    string PolicyVersion);

public sealed record InternalTaskResponse(OwnerTaskResponse Task);
public sealed record InternalAgentRunResponse(AgentRunResponse Run);
