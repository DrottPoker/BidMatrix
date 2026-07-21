using BidMatrix.Api.Security;
using BidMatrix.Application.Agents;
using BidMatrix.Application.Internal;
using BidMatrix.Application.Owner;
using BidMatrix.Application.Tools;
using BidMatrix.Contracts.Agents;
using BidMatrix.Contracts.Internal;
using BidMatrix.Contracts.Owner;
using BidMatrix.Contracts.Tools;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class InternalFoundationEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixInternalFoundationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var internalRoutes = endpoints.MapGroup("/internal/v1")
            .RequireAuthorization(BidMatrixPolicies.InternalService)
            .WithTags("Internal", "Foundation");

        internalRoutes.MapGet("/tasks/{taskId:guid}", GetTaskAsync)
            .WithName("GetInternalTask");
        internalRoutes.MapPost("/tasks", CreateTaskAsync)
            .WithName("CreateInternalTask");
        internalRoutes.MapPatch("/tasks/{taskId:guid}/status", UpdateTaskStatusAsync)
            .WithName("UpdateInternalTaskStatus");

        internalRoutes.MapGet("/context/company-constitution", GetCompanyConstitutionAsync)
            .WithName("GetInternalCompanyConstitution");
        internalRoutes.MapGet("/context/metrics", GetMetricsAsync)
            .WithName("GetInternalMetrics");
        internalRoutes.MapPost("/knowledge/search", SearchKnowledgeAsync)
            .WithName("SearchInternalKnowledge");

        internalRoutes.MapPost("/agent-runs", CreateAgentRunAsync)
            .WithName("CreateInternalAgentRun");
        internalRoutes.MapPatch("/agent-runs/{runId:guid}", UpdateAgentRunAsync)
            .WithName("UpdateInternalAgentRun");
        internalRoutes.MapPost("/artifacts", CreateArtifactAsync)
            .WithName("CreateInternalArtifact");

        internalRoutes.MapGet("/tools/catalog", ListToolsAsync)
            .WithName("ListInternalTools");
        internalRoutes.MapPost("/tools/evaluate", EvaluateToolAsync)
            .WithName("EvaluateInternalTool");
        internalRoutes.MapPost("/tools/execute", ExecuteToolAsync)
            .WithName("ExecuteInternalTool");

        internalRoutes.MapPost("/approvals", CreateApprovalAsync)
            .WithName("CreateInternalApproval");
        internalRoutes.MapGet("/approvals/{approvalId:guid}", GetApprovalAsync)
            .WithName("GetInternalApproval");

        return endpoints;
    }

    private static async Task<IResult> GetTaskAsync(
        Guid taskId,
        [FromQuery] Guid organizationId,
        IInternalFoundationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RequireGuid(organizationId, "organizationId");
            var task = await service.GetTaskAsync(organizationId, taskId, cancellationToken);
            return task is null
                ? Results.NotFound()
                : Results.Ok(new InternalTaskResponse(ToResponse(task)));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static Task<IResult> CreateTaskAsync(
        [FromBody] ToolGatewayRequestContract request,
        IToolGatewayService service,
        CancellationToken cancellationToken) =>
        ExecuteGatewayAsync(request, service, "task.create", cancellationToken);

    private static async Task<IResult> UpdateTaskStatusAsync(
        Guid taskId,
        [FromBody] UpdateInternalTaskStatusRequest request,
        IInternalFoundationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = await service.UpdateTaskStatusAsync(new UpdateInternalTaskStatusCommand(
                ParseGuid(request.OrganizationId, "organizationId"),
                taskId,
                request.Status,
                request.ExpectedVersion,
                request.ErrorCode,
                request.ErrorMessage,
                request.CorrelationId), cancellationToken);
            return Results.Ok(new InternalTaskResponse(ToResponse(task)));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetCompanyConstitutionAsync(
        IInternalFoundationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetCompanyConstitutionAsync(cancellationToken));

    private static async Task<IResult> GetMetricsAsync(
        [FromQuery] Guid organizationId,
        IInternalFoundationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RequireGuid(organizationId, "organizationId");
            return Results.Ok(await service.GetMetricsAsync(organizationId, cancellationToken));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> SearchKnowledgeAsync(
        [FromBody] InternalKnowledgeSearchRequest request,
        IInternalFoundationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.SearchKnowledgeAsync(request.Query, cancellationToken));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> CreateAgentRunAsync(
        [FromBody] CreateInternalAgentRunRequest request,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var taskId = ParseGuid(request.TaskId, "taskId");
            var preparation = await service.PrepareAsync(
                ParseGuid(request.OrganizationId, "organizationId"),
                taskId,
                request.WorkflowId,
                request.CorrelationId,
                request.RuntimeMode,
                request.ModelName,
                cancellationToken);
            return Results.Created(
                $"/internal/v1/agent-runs/{preparation.AgentRunId}",
                ToResponse(preparation));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> UpdateAgentRunAsync(
        Guid runId,
        [FromBody] UpdateInternalAgentRunRequest request,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var taskId = ParseGuid(request.TaskId, "taskId");
            var organizationId = ParseGuid(request.OrganizationId, "organizationId");
            if (string.Equals(request.Status, "completed", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(request.OutputArtifactId) || request.Output is null)
                {
                    throw new ToolGatewayException(
                        "invalid_agent_completion",
                        "outputArtifactId and output are required for a completed agent run.",
                        400);
                }

                var completed = await service.CompleteAsync(new CompleteAgentRunCommand(
                    taskId,
                    organizationId,
                    runId,
                    ParseGuid(request.OutputArtifactId, "outputArtifactId"),
                    request.Output.Value,
                    request.RequestCount,
                    request.InputTokens,
                    request.OutputTokens,
                    request.ReasoningTokens,
                    request.CorrelationId), cancellationToken);
                return Results.Ok(new InternalAgentRunResponse(ToResponse(completed)));
            }

            if (string.Equals(request.Status, "failed", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(request.FailureCode) ||
                    string.IsNullOrWhiteSpace(request.FailureMessage))
                {
                    throw new ToolGatewayException(
                        "invalid_agent_failure",
                        "failureCode and failureMessage are required for a failed agent run.",
                        400);
                }

                var failed = await service.FailAsync(new FailAgentRunCommand(
                    taskId,
                    organizationId,
                    runId,
                    request.FailureCode,
                    request.FailureMessage,
                    request.CorrelationId), cancellationToken);
                return failed is null
                    ? Results.NoContent()
                    : Results.Ok(new InternalAgentRunResponse(ToResponse(failed)));
            }

            throw new ToolGatewayException(
                "unsupported_agent_run_status",
                "The generic baseline agent-run update accepts only completed or failed.",
                400);
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static Task<IResult> CreateArtifactAsync(
        [FromBody] ToolGatewayRequestContract request,
        IToolGatewayService service,
        CancellationToken cancellationToken) =>
        ExecuteGatewayAsync(request, service, "artifact.createDraft", cancellationToken);

    private static async Task<IResult> ListToolsAsync(
        IInternalFoundationService service,
        CancellationToken cancellationToken)
    {
        var tools = await service.ListToolsAsync(cancellationToken);
        return Results.Ok(new InternalToolCatalogResponse(tools.Select(tool => new InternalToolResponse(
            tool.ToolKey,
            tool.DisplayName,
            tool.Description,
            tool.RiskLevel,
            tool.SideEffectClass,
            tool.Enabled,
            tool.ApprovalMode)).ToArray()));
    }

    private static async Task<IResult> EvaluateToolAsync(
        [FromBody] InternalToolEvaluationRequest request,
        IInternalFoundationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.AgentKey) || string.IsNullOrWhiteSpace(request.ToolKey))
            {
                throw new ToolGatewayException(
                    "invalid_tool_evaluation",
                    "agentKey and toolKey are required.",
                    400);
            }

            var result = await service.EvaluateToolAsync(new InternalPolicyEvaluationCommand(
                ParseGuid(request.OrganizationId, "organizationId"),
                ParseGuid(request.TaskId, "taskId"),
                ParseGuid(request.AgentRunId, "agentRunId"),
                request.AgentKey,
                request.ToolKey), cancellationToken);
            return Results.Ok(new InternalToolEvaluationResponse(
                result.Decision,
                result.ReasonCode,
                result.TechnicallyEnabled,
                result.PolicyVersion));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static Task<IResult> ExecuteToolAsync(
        [FromBody] ToolGatewayRequestContract request,
        IToolGatewayService service,
        CancellationToken cancellationToken) =>
        ExecuteGatewayAsync(request, service, null, cancellationToken);

    private static Task<IResult> CreateApprovalAsync(
        [FromBody] ToolGatewayRequestContract request,
        IToolGatewayService service,
        CancellationToken cancellationToken) =>
        ExecuteGatewayAsync(request, service, "approval.request", cancellationToken);

    private static async Task<IResult> GetApprovalAsync(
        Guid approvalId,
        [FromQuery] Guid organizationId,
        IApprovalService service,
        CancellationToken cancellationToken)
    {
        try
        {
            RequireGuid(organizationId, "organizationId");
            var approval = await service.GetAsync(approvalId, organizationId, cancellationToken);
            return approval is null ? Results.NotFound() : Results.Ok(ToResponse(approval));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ExecuteGatewayAsync(
        ToolGatewayRequestContract request,
        IToolGatewayService service,
        string? requiredToolKey,
        CancellationToken cancellationToken)
    {
        try
        {
            if (requiredToolKey is not null &&
                !string.Equals(request.ToolKey, requiredToolKey, StringComparison.Ordinal))
            {
                throw new ToolGatewayException(
                    "tool_route_mismatch",
                    $"This route accepts only the {requiredToolKey} tool.",
                    400);
            }

            var result = await service.ExecuteAsync(new ToolGatewayRequest(
                ParseGuid(request.RequestId, "requestId"),
                ParseGuid(request.TaskId, "taskId"),
                ParseGuid(request.AgentRunId, "agentRunId"),
                request.AgentKey,
                request.ToolKey,
                request.IdempotencyKey,
                request.Arguments,
                ParseGuid(request.Context.OrganizationId, "organizationId"),
                request.Context.CorrelationId), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static OwnerTaskResponse ToResponse(OwnerTaskRecord task) => new(
        task.Id.ToString(),
        task.OrganizationId?.ToString(),
        task.GoalId?.ToString(),
        task.Type,
        task.Title,
        task.Description,
        task.Priority,
        task.Status,
        task.AssignedAgentKey,
        task.Input,
        task.Constraints,
        task.ResultArtifactId?.ToString(),
        task.ErrorCode,
        task.ErrorMessage,
        task.CreatedAt,
        task.StartedAt,
        task.CompletedAt,
        task.UpdatedAt,
        task.Version);

    private static AgentTaskPreparationResponse ToResponse(AgentTaskPreparation preparation) => new(
        preparation.TaskId.ToString(),
        preparation.OrganizationId.ToString(),
        preparation.AgentRunId.ToString(),
        preparation.AgentKey,
        preparation.AgentVersion,
        preparation.ModelName,
        preparation.PromptVersion,
        preparation.AllowedTools,
        preparation.Input,
        preparation.WorkflowId,
        preparation.CorrelationId);

    private static AgentRunResponse ToResponse(AgentRunRecord run) => new(
        run.Id.ToString(),
        run.OrganizationId.ToString(),
        run.TaskId.ToString(),
        run.AgentKey,
        run.Status,
        run.ModelName,
        run.PromptVersion,
        run.OutputArtifactId?.ToString(),
        run.InputTokens,
        run.OutputTokens,
        run.StartedAt,
        run.CompletedAt,
        run.FailureCode,
        run.FailureMessage);

    private static ToolGatewayResponse ToResponse(ToolGatewayResult result) => new(
        result.Decision,
        result.ToolCallId.ToString(),
        result.PolicyVersion,
        result.ApprovalId?.ToString(),
        result.ReasonCode,
        result.InputHash,
        result.ExecutionStatus,
        result.Output);

    private static ApprovalResponse ToResponse(ApprovalRecord approval) => new(
        approval.Id.ToString(),
        approval.OrganizationId.ToString(),
        approval.ToolCallId?.ToString(),
        approval.TaskId?.ToString(),
        approval.ActionType,
        approval.Status,
        approval.Summary,
        approval.NormalizedPayload,
        approval.PayloadHash,
        approval.PolicyVersion,
        approval.RiskLevel,
        approval.TechnicallyEnabled,
        approval.RequestedAt,
        approval.ExpiresAt,
        approval.DecidedByUserId?.ToString(),
        approval.DecidedAt,
        approval.DecisionNote,
        approval.ExecutionStatus,
        approval.Version,
        approval.SupersedesApprovalId?.ToString());

    private static Guid ParseGuid(string value, string fieldName) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new ToolGatewayException(
                "invalid_identifier",
                $"{fieldName} must be a valid non-empty UUID.",
                400);

    private static void RequireGuid(Guid value, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ToolGatewayException(
                "invalid_identifier",
                $"{fieldName} must be a valid non-empty UUID.",
                400);
        }
    }

    private static IResult Problem(ToolGatewayException exception) => Results.Problem(
        statusCode: exception.StatusCode,
        title: exception.Code,
        detail: exception.Message);
}
