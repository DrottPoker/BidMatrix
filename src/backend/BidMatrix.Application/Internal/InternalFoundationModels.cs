using System.Text.Json;
using BidMatrix.Application.Owner;

namespace BidMatrix.Application.Internal;

public sealed record UpdateInternalTaskStatusCommand(
    Guid OrganizationId,
    Guid TaskId,
    string Status,
    int ExpectedVersion,
    string? ErrorCode,
    string? ErrorMessage,
    string CorrelationId);

public sealed record InternalToolCatalogRecord(
    string ToolKey,
    string DisplayName,
    string Description,
    string RiskLevel,
    string SideEffectClass,
    bool Enabled,
    string ApprovalMode);

public sealed record InternalPolicyEvaluationCommand(
    Guid OrganizationId,
    Guid TaskId,
    Guid AgentRunId,
    string AgentKey,
    string ToolKey);

public sealed record InternalPolicyEvaluationRecord(
    string Decision,
    string ReasonCode,
    bool TechnicallyEnabled,
    string PolicyVersion);

public interface IInternalFoundationService
{
    Task<OwnerTaskRecord?> GetTaskAsync(Guid organizationId, Guid taskId, CancellationToken cancellationToken = default);
    Task<OwnerTaskRecord> UpdateTaskStatusAsync(UpdateInternalTaskStatusCommand command, CancellationToken cancellationToken = default);
    Task<JsonElement> GetCompanyConstitutionAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> GetMetricsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<JsonElement> SearchKnowledgeAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InternalToolCatalogRecord>> ListToolsAsync(CancellationToken cancellationToken = default);
    Task<InternalPolicyEvaluationRecord> EvaluateToolAsync(InternalPolicyEvaluationCommand command, CancellationToken cancellationToken = default);
}
