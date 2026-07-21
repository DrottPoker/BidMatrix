using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Owner;
using BidMatrix.Application.Agents;
using BidMatrix.Contracts.Agents;
using BidMatrix.Contracts.Owner;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class OwnerConsoleEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixOwnerConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var owner = endpoints.MapGroup("/owner/v1")
            .RequireAuthorization(BidMatrixPolicies.PlatformOwner)
            .WithTags("Owner");

        owner.MapGet("/dashboard", GetDashboardAsync).WithName("GetOperationalOwnerDashboard");
        owner.MapGet("/tasks", ListTasksAsync).WithName("ListOwnerTasks");
        owner.MapPost("/tasks", CreateTaskAsync).AddEndpointFilter<ValidateAntiforgeryFilter>().WithName("CreateOwnerTask");
        owner.MapGet("/tasks/{taskId:guid}", GetTaskAsync).WithName("GetOwnerTask");
        owner.MapPost("/tasks/{taskId:guid}/cancel", CancelTaskAsync).AddEndpointFilter<ValidateAntiforgeryFilter>().WithName("CancelOwnerTask");

        owner.MapGet("/agents", ListAgentsAsync).WithName("ListOwnerAgents");
        owner.MapGet("/agents/{agentKey}", GetAgentAsync).WithName("GetOwnerAgent");
        owner.MapGet("/runs", ListRunsAsync).WithName("ListOwnerRuns");
        owner.MapGet("/runs/{runId:guid}", GetRunAsync).WithName("GetOwnerRun");
        owner.MapGet("/agent-runs/{runId:guid}", GetRunAsync).WithName("GetOwnerAgentRun");
        owner.MapGet("/workflows", ListWorkflowsAsync).WithName("ListOwnerWorkflows");
        owner.MapGet("/workflows/{workflowRunId:guid}", GetWorkflowAsync).WithName("GetOwnerWorkflow");
        owner.MapGet("/artifacts", ListArtifactsAsync).WithName("ListOwnerArtifacts");
        owner.MapGet("/artifacts/{artifactId:guid}", GetArtifactAsync).WithName("GetOwnerArtifact");
        owner.MapGet("/audit", ListAuditAsync).WithName("ListOwnerAudit");

        owner.MapGet("/goals", ListGoalsAsync).WithName("ListOwnerGoals");
        owner.MapPost("/goals", CreateGoalAsync).AddEndpointFilter<ValidateAntiforgeryFilter>().WithName("CreateOwnerGoal");
        owner.MapPatch("/goals/{goalId:guid}", UpdateGoalAsync).AddEndpointFilter<ValidateAntiforgeryFilter>().WithName("UpdateOwnerGoal");

        owner.MapGet("/system-controls", ListSystemControlsAsync).WithName("ListOwnerSystemControls");
        owner.MapPatch("/system-controls/{controlKey}", UpdateSystemControlAsync).AddEndpointFilter<ValidateAntiforgeryFilter>().WithName("UpdateOwnerSystemControl");

        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
        {
            MapDemo(owner, "/demo/executive-brief", "executive", "CreateExecutiveBriefDemo");
            MapDemo(owner, "/demo/support-draft", "support", "CreateSupportDraftDemo");
            MapDemo(owner, "/demo/product-analysis", "product-analyst", "CreateProductAnalysisDemo");
            MapDemo(owner, "/demo/engineering-plan", "engineering", "CreateEngineeringPlanDemo");
        }

        return endpoints;
    }

    private static void MapDemo(RouteGroupBuilder owner, string route, string agentKey, string endpointName)
    {
        owner.MapPost(route, async (
                [FromBody] CreateAgentDemoRequest? request,
                ClaimsPrincipal principal,
                HttpContext context,
                IAgentRuntimeService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var idempotencyKey = RequiredIdempotencyKey(context);
                    var task = await service.CreateDemoAsync(new AgentDemoCreateCommand(
                        GetOrganizationId(principal), GetUserId(principal), agentKey, request?.Input,
                        idempotencyKey, context.TraceIdentifier), cancellationToken);
                    return Results.Accepted($"/owner/v1/tasks/{task.TaskId}", new AgentDemoTaskResponse(
                        task.TaskId.ToString(), task.OrganizationId.ToString(), task.AgentKey,
                        task.Status, task.WorkflowId, task.CreatedAt));
                }
                catch (OwnerConsoleException exception)
                {
                    return Problem(exception);
                }
            })
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName(endpointName);
    }

    private static async Task<IResult> GetDashboardAsync(
        ClaimsPrincipal principal,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        var dashboard = await service.GetDashboardAsync(GetOrganizationId(principal), cancellationToken);
        return Results.Ok(new OwnerDashboardResponse(
            dashboard.AnalysesByStatus, dashboard.OpenTasks, dashboard.PendingApprovals,
            dashboard.WorkflowFailures, dashboard.RecentAgentRuns.Select(ToResponse).ToArray(),
            dashboard.SystemControls.Select(ToResponse).ToArray(), dashboard.AuditChainValid,
            dashboard.SystemControls.Any(control => control.ControlKey == "systemDraftOnlyMode" && control.Enabled),
            dashboard.SystemControls.Any(control => control.ControlKey == "externalToolExecutionEnabled" && !control.Enabled),
            dashboard.GeneratedAt));
    }

    private static async Task<IResult> ListTasksAsync(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? agentKey,
        [FromQuery] string? priority,
        ClaimsPrincipal principal,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var tasks = await service.ListTasksAsync(GetOrganizationId(principal), status, type, agentKey, priority, cancellationToken);
            return Results.Ok(new OwnerTaskListResponse(tasks.Select(ToResponse).ToArray()));
        }
        catch (OwnerConsoleException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetTaskAsync(
        Guid taskId,
        ClaimsPrincipal principal,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        var task = await service.GetTaskAsync(GetOrganizationId(principal), taskId, cancellationToken);
        return task is null ? Results.NotFound() : Results.Ok(ToResponse(task));
    }

    private static async Task<IResult> CreateTaskAsync(
        [FromBody] CreateOwnerTaskRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = await service.CreateTaskAsync(new CreateOwnerTaskCommand(
                GetOrganizationId(principal), GetUserId(principal), ParseOptionalGuid(request.GoalId, "goalId"),
                request.Type, request.Title, request.Description, request.Priority, request.AssignedAgentKey,
                request.Input, request.Constraints, RequiredIdempotencyKey(context), context.TraceIdentifier),
                cancellationToken);
            return Results.Created($"/owner/v1/tasks/{task.Id}", ToResponse(task));
        }
        catch (OwnerConsoleException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> CancelTaskAsync(
        Guid taskId,
        [FromBody] CancelOwnerTaskRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var task = await service.CancelTaskAsync(new CancelOwnerTaskCommand(
                GetOrganizationId(principal), GetUserId(principal), taskId, request.ExpectedVersion,
                request.Note, context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(task));
        }
        catch (OwnerConsoleException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ListAgentsAsync(IOwnerConsoleService service, CancellationToken cancellationToken) =>
        Results.Ok(new OwnerAgentListResponse((await service.ListAgentsAsync(cancellationToken)).Select(ToResponse).ToArray()));

    private static async Task<IResult> GetAgentAsync(string agentKey, IOwnerConsoleService service, CancellationToken cancellationToken)
    {
        var agent = await service.GetAgentAsync(agentKey, cancellationToken);
        return agent is null ? Results.NotFound() : Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult> ListRunsAsync(ClaimsPrincipal principal, IOwnerConsoleService service, CancellationToken cancellationToken) =>
        Results.Ok(new OwnerAgentRunListResponse((await service.ListAgentRunsAsync(GetOrganizationId(principal), cancellationToken)).Select(ToResponse).ToArray()));

    private static async Task<IResult> GetRunAsync(Guid runId, ClaimsPrincipal principal, IOwnerConsoleService service, CancellationToken cancellationToken)
    {
        var run = await service.GetAgentRunAsync(GetOrganizationId(principal), runId, cancellationToken);
        return run is null ? Results.NotFound() : Results.Ok(ToResponse(run));
    }

    private static async Task<IResult> ListWorkflowsAsync(ClaimsPrincipal principal, IOwnerConsoleService service, CancellationToken cancellationToken) =>
        Results.Ok(new OwnerWorkflowListResponse((await service.ListWorkflowsAsync(GetOrganizationId(principal), cancellationToken)).Select(ToResponse).ToArray()));

    private static async Task<IResult> GetWorkflowAsync(Guid workflowRunId, ClaimsPrincipal principal, IOwnerConsoleService service, CancellationToken cancellationToken)
    {
        var workflow = await service.GetWorkflowAsync(GetOrganizationId(principal), workflowRunId, cancellationToken);
        return workflow is null ? Results.NotFound() : Results.Ok(ToResponse(workflow));
    }

    private static async Task<IResult> ListArtifactsAsync(ClaimsPrincipal principal, IOwnerConsoleService service, CancellationToken cancellationToken) =>
        Results.Ok(new OwnerArtifactListResponse((await service.ListArtifactsAsync(GetOrganizationId(principal), cancellationToken)).Select(ToResponse).ToArray()));

    private static async Task<IResult> GetArtifactAsync(Guid artifactId, ClaimsPrincipal principal, IOwnerConsoleService service, CancellationToken cancellationToken)
    {
        var artifact = await service.GetArtifactAsync(GetOrganizationId(principal), artifactId, cancellationToken);
        return artifact is null ? Results.NotFound() : Results.Ok(ToResponse(artifact));
    }

    private static async Task<IResult> ListAuditAsync(IOwnerConsoleService service, CancellationToken cancellationToken)
    {
        var events = await service.ListAuditAsync(cancellationToken);
        return Results.Ok(new OwnerAuditListResponse(events.All(item => item.ChainLinkValid), events.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> ListGoalsAsync(IOwnerConsoleService service, CancellationToken cancellationToken) =>
        Results.Ok(new OwnerGoalListResponse((await service.ListGoalsAsync(cancellationToken)).Select(ToResponse).ToArray()));

    private static async Task<IResult> CreateGoalAsync(
        [FromBody] CreateOwnerGoalRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var goal = await service.CreateGoalAsync(new CreateOwnerGoalCommand(
                GetUserId(principal), request.Title, request.Description, request.MetricKey,
                request.TargetValue, request.TargetDate, request.Status, request.Constraints,
                context.TraceIdentifier), cancellationToken);
            return Results.Created($"/owner/v1/goals/{goal.Id}", ToResponse(goal));
        }
        catch (OwnerConsoleException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> UpdateGoalAsync(
        Guid goalId,
        [FromBody] UpdateOwnerGoalRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var goal = await service.UpdateGoalAsync(new UpdateOwnerGoalCommand(
                GetUserId(principal), goalId, request.Title, request.Description, request.MetricKey,
                request.TargetValue, request.TargetDate, request.Status, request.Constraints,
                request.ExpectedVersion, context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(goal));
        }
        catch (OwnerConsoleException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ListSystemControlsAsync(IOwnerConsoleService service, CancellationToken cancellationToken) =>
        Results.Ok(new OwnerSystemControlListResponse((await service.ListSystemControlsAsync(cancellationToken)).Select(ToResponse).ToArray()));

    private static async Task<IResult> UpdateSystemControlAsync(
        string controlKey,
        [FromBody] UpdateSystemControlRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IOwnerConsoleService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var control = await service.UpdateSystemControlAsync(new UpdateSystemControlCommand(
                GetUserId(principal), controlKey, request.Enabled, request.ExpectedVersion,
                request.Confirmation, context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(control));
        }
        catch (OwnerConsoleException exception)
        {
            return Problem(exception);
        }
    }

    private static OwnerTaskResponse ToResponse(OwnerTaskRecord task) => new(
        task.Id.ToString(), task.OrganizationId?.ToString(), task.GoalId?.ToString(), task.Type,
        task.Title, task.Description, task.Priority, task.Status, task.AssignedAgentKey, task.Input,
        task.Constraints, task.ResultArtifactId?.ToString(), task.ErrorCode, task.ErrorMessage,
        task.CreatedAt, task.StartedAt, task.CompletedAt, task.UpdatedAt, task.Version);

    private static OwnerAgentResponse ToResponse(OwnerAgentDefinitionRecord agent) => new(
        agent.AgentKey, agent.DisplayName, agent.Description, agent.Status, agent.Version,
        agent.PromptVersion, agent.ModelKey, agent.ToolPermissions, agent.TotalRuns, agent.FailedRuns,
        agent.InputTokens, agent.OutputTokens, agent.LastRunAt);

    private static OwnerAgentRunResponse ToResponse(OwnerAgentRunRecord run) => new(
        run.Id.ToString(), run.OrganizationId.ToString(), run.TaskId.ToString(), run.AgentKey,
        run.Status, run.ModelName, run.PromptVersion, run.OutputArtifactId?.ToString(),
        run.InputTokens, run.OutputTokens, run.StartedAt, run.CompletedAt, run.FailureCode, run.FailureMessage);

    private static OwnerWorkflowResponse ToResponse(OwnerWorkflowRecord workflow) => new(
        workflow.Id.ToString(), workflow.OrganizationId.ToString(), workflow.WorkflowType,
        workflow.WorkflowId, workflow.TemporalRunId, workflow.TaskId?.ToString(), workflow.Status,
        workflow.Input, workflow.Result, workflow.StartedAt, workflow.CompletedAt, workflow.UpdatedAt);

    private static OwnerArtifactResponse ToResponse(OwnerArtifactRecord artifact) => new(
        artifact.Id.ToString(), artifact.OrganizationId?.ToString(), artifact.ArtifactType,
        artifact.Title, artifact.ContentType, artifact.InlineContent, artifact.Sha256,
        artifact.Sensitivity, artifact.CreatedByType, artifact.CreatedById, artifact.CreatedAt,
        artifact.SupersedesArtifactId?.ToString());

    private static OwnerAuditEventResponse ToResponse(OwnerAuditEventRecord audit) => new(
        audit.Id.ToString(), audit.SequenceNumber, audit.ActorType, audit.ActorId, audit.Action,
        audit.TargetType, audit.TargetId, audit.OrganizationId?.ToString(), audit.RequestId,
        audit.TraceId, audit.Summary, audit.PreviousHash, audit.EventHash, audit.ChainLinkValid,
        audit.CreatedAt);

    private static OwnerGoalResponse ToResponse(OwnerGoalRecord goal) => new(
        goal.Id.ToString(), goal.Title, goal.Description, goal.MetricKey, goal.TargetValue,
        goal.TargetDate, goal.Status, goal.Constraints, goal.CreatedAt, goal.UpdatedAt, goal.Version);

    private static OwnerSystemControlResponse ToResponse(OwnerSystemControlRecord control) => new(
        control.ControlKey, control.Enabled, control.Value, control.UpdatedByUserId.ToString(),
        control.UpdatedAt, control.Version, control.LockedForF0);

    private static string RequiredIdempotencyKey(HttpContext context) =>
        context.Request.Headers.TryGetValue("Idempotency-Key", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : throw new OwnerConsoleException("idempotency_key_required", "Idempotency-Key is required.", 400);

    private static Guid? ParseOptionalGuid(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
                ? parsed
                : throw new OwnerConsoleException("invalid_identifier", $"{fieldName} must be a UUID.", 400);

    private static Guid GetOrganizationId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(BidMatrixClaimTypes.OrganizationId), out var value)
            ? value
            : throw new InvalidOperationException("Authenticated principal is missing organization context.");

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var value)
            ? value
            : throw new InvalidOperationException("Authenticated principal is missing a user identifier.");

    private static IResult Problem(OwnerConsoleException exception) => Results.Problem(
        statusCode: exception.StatusCode, title: exception.Code, detail: exception.Message);
}
