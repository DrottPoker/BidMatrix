using System.Text.Json;

namespace BidMatrix.Application.Tools;

public static class ToolDecisions
{
    public const string Allowed = "allowed";
    public const string Denied = "denied";
    public const string ApprovalRequired = "approvalRequired";
    public const string Disabled = "disabled";
    public const string AlreadyExecuted = "alreadyExecuted";
    public const string Invalid = "invalid";
}

public sealed record ToolGatewayRequest(
    Guid RequestId,
    Guid TaskId,
    Guid AgentRunId,
    string AgentKey,
    string ToolKey,
    string IdempotencyKey,
    JsonElement Arguments,
    Guid OrganizationId,
    string CorrelationId);

public sealed record ToolGatewayResult(
    string Decision,
    Guid ToolCallId,
    string PolicyVersion,
    Guid? ApprovalId,
    string ReasonCode,
    string InputHash,
    string ExecutionStatus,
    JsonElement? Output);

public sealed record ToolDefinitionSnapshot(
    Guid Id,
    string ToolKey,
    string RiskLevel,
    string SideEffectClass,
    bool Enabled,
    string ApprovalMode);

public sealed record PolicyEvaluationContext(
    ToolDefinitionSnapshot Tool,
    string AgentKey,
    string TaskType,
    bool OwnerCreatedTask,
    IReadOnlyDictionary<string, bool> Controls,
    string EnvironmentName);

public sealed record PolicyEvaluation(
    string Decision,
    string ReasonCode,
    bool TechnicallyEnabled);

public interface IPolicyEngine
{
    PolicyEvaluation Evaluate(PolicyEvaluationContext context);
}

public interface IToolGatewayService
{
    Task<ToolGatewayResult> ExecuteAsync(
        ToolGatewayRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ToolGatewayException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
