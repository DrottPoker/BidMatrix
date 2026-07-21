using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Agents;
using BidMatrix.Application.Tools;
using BidMatrix.Contracts.Agents;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class AgentRuntimeEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixAgentRuntimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            endpoints.MapPost("/owner/v1/agent-demos/{agentKey}", CreateDemoAsync)
                .RequireAuthorization(BidMatrixPolicies.PlatformOwner)
                .AddEndpointFilter<ValidateAntiforgeryFilter>()
                .WithTags("Owner", "Agents")
                .WithName("CreateAgentDemo");
        }
        endpoints.MapGet("/owner/v1/agent-runs", ListRunsAsync)
            .RequireAuthorization(BidMatrixPolicies.PlatformOwner)
            .WithTags("Owner", "Agents")
            .WithName("ListAgentRuns");

        var internalRoutes = endpoints.MapGroup("/internal/v1/agent-tasks")
            .RequireAuthorization(BidMatrixPolicies.InternalService)
            .WithTags("Internal", "Agents");
        internalRoutes.MapPost("/{taskId:guid}/prepare", PrepareAsync)
            .WithName("PrepareAgentTask");
        internalRoutes.MapPost("/{taskId:guid}/complete", CompleteAsync)
            .WithName("CompleteAgentTask");
        internalRoutes.MapPost("/{taskId:guid}/fail", FailAsync)
            .WithName("FailAgentTask");

        return endpoints;
    }

    private static async Task<IResult> CreateDemoAsync(
        string agentKey,
        [FromBody] CreateAgentDemoRequest? request,
        HttpContext context,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey))
            {
                throw new ToolGatewayException(
                    "idempotency_key_required",
                    "Idempotency-Key is required when creating an agent demonstration.",
                    400);
            }

            var result = await service.CreateDemoAsync(new AgentDemoCreateCommand(
                GetOrganizationId(context.User),
                GetUserId(context.User),
                agentKey,
                request?.Input,
                idempotencyKey.ToString(),
                context.TraceIdentifier), cancellationToken);
            return Results.Accepted($"/owner/v1/agent-runs?taskId={result.TaskId}", ToResponse(result));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ListRunsAsync(
        [FromQuery] Guid? organizationId,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        var runs = await service.ListRunsAsync(organizationId, cancellationToken);
        return Results.Ok(new AgentRunListResponse(runs.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> PrepareAsync(
        Guid taskId,
        [FromBody] PrepareAgentTaskRequest request,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var preparation = await service.PrepareAsync(
                ParseGuid(request.OrganizationId, "organizationId"),
                taskId,
                request.WorkflowId,
                request.CorrelationId,
                request.RuntimeMode,
                request.ModelName,
                cancellationToken);
            return Results.Ok(ToResponse(preparation));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> CompleteAsync(
        Guid taskId,
        [FromBody] CompleteAgentRunRequest request,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CompleteAsync(new CompleteAgentRunCommand(
                taskId,
                ParseGuid(request.OrganizationId, "organizationId"),
                ParseGuid(request.AgentRunId, "agentRunId"),
                ParseGuid(request.OutputArtifactId, "outputArtifactId"),
                request.Output,
                request.RequestCount,
                request.InputTokens,
                request.OutputTokens,
                request.ReasoningTokens,
                request.CorrelationId), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> FailAsync(
        Guid taskId,
        [FromBody] FailAgentRunRequest request,
        IAgentRuntimeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.FailAsync(new FailAgentRunCommand(
                taskId,
                ParseGuid(request.OrganizationId, "organizationId"),
                string.IsNullOrWhiteSpace(request.AgentRunId)
                    ? null
                    : ParseGuid(request.AgentRunId, "agentRunId"),
                request.FailureCode,
                request.FailureMessage,
                request.CorrelationId), cancellationToken);
            return result is null ? Results.NoContent() : Results.Ok(ToResponse(result));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static AgentDemoTaskResponse ToResponse(AgentDemoTaskRecord task) => new(
        task.TaskId.ToString(),
        task.OrganizationId.ToString(),
        task.AgentKey,
        task.Status,
        task.WorkflowId,
        task.CreatedAt);

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

    private static Guid GetOrganizationId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(BidMatrixClaimTypes.OrganizationId), out var organizationId)
            ? organizationId
            : throw new InvalidOperationException("Authenticated principal is missing organization context.");

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated principal is missing a user identifier.");

    private static Guid ParseGuid(string value, string fieldName) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new ToolGatewayException(
                "invalid_agent_request",
                $"{fieldName} must be a valid non-empty UUID.",
                400);

    private static IResult Problem(ToolGatewayException exception) => Results.Problem(
        statusCode: exception.StatusCode,
        title: exception.Code,
        detail: exception.Message);
}
