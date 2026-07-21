using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Tools;
using BidMatrix.Contracts.Tools;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class ToolGatewayEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixToolGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/v1/tool-gateway/calls", ExecuteToolAsync)
            .RequireAuthorization(BidMatrixPolicies.InternalService)
            .WithTags("Internal", "Tool Gateway")
            .WithName("ExecuteToolGatewayCall");

        var owner = endpoints.MapGroup("/owner/v1/approvals")
            .RequireAuthorization(BidMatrixPolicies.PlatformOwner)
            .WithTags("Owner", "Approvals");
        owner.MapGet("", ListApprovalsAsync)
            .WithName("ListApprovals");
        owner.MapGet("/{approvalId:guid}", GetApprovalAsync)
            .WithName("GetApproval");
        owner.MapPost("/{approvalId:guid}/decision", DecideApprovalAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("DecideApproval");
        owner.MapPost("/{approvalId:guid}/approve", ApproveApprovalAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("ApproveApproval");
        owner.MapPost("/{approvalId:guid}/reject", RejectApprovalAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("RejectApproval");

        endpoints.MapPost("/internal/v1/approvals/{approvalId:guid}/expire", ExpireApprovalAsync)
            .RequireAuthorization(BidMatrixPolicies.InternalService)
            .WithTags("Internal", "Approvals")
            .WithName("ExpireApproval");

        return endpoints;
    }

    private static async Task<IResult> ExecuteToolAsync(
        [FromBody] ToolGatewayRequestContract request,
        IToolGatewayService service,
        CancellationToken cancellationToken)
    {
        try
        {
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

    private static async Task<IResult> ListApprovalsAsync(
        [FromQuery] Guid? organizationId,
        [FromQuery] string? status,
        IApprovalService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var approvals = await service.ListAsync(organizationId, status, cancellationToken);
            return Results.Ok(new ApprovalListResponse(approvals.Select(ToResponse).ToArray()));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetApprovalAsync(
        Guid approvalId,
        IApprovalService service,
        CancellationToken cancellationToken)
    {
        var approval = await service.GetAsync(approvalId, null, cancellationToken);
        return approval is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Approval not found")
            : Results.Ok(ToResponse(approval));
    }

    private static async Task<IResult> DecideApprovalAsync(
        Guid approvalId,
        [FromBody] ApprovalDecisionRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IApprovalService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var approval = await service.GetAsync(approvalId, null, cancellationToken);
            if (approval is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Approval not found");
            }

            var result = await service.DecideAsync(new ApprovalDecisionCommand(
                approvalId,
                approval.OrganizationId,
                GetUserId(principal),
                request.Decision,
                request.Payload,
                request.ExpectedVersion,
                request.Note,
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ExpireApprovalAsync(
        Guid approvalId,
        IApprovalService service,
        CancellationToken cancellationToken)
    {
        var approval = await service.ExpireAsync(approvalId, cancellationToken);
        return approval is null ? Results.NotFound() : Results.Ok(ToResponse(approval));
    }

    private static Task<IResult> ApproveApprovalAsync(
        Guid approvalId,
        [FromBody] OwnerApprovalActionRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IApprovalService service,
        CancellationToken cancellationToken) =>
        DecideWithFixedActionAsync(
            approvalId, request, ApprovalDecisions.Approve, principal, context, service, cancellationToken);

    private static Task<IResult> RejectApprovalAsync(
        Guid approvalId,
        [FromBody] OwnerApprovalActionRequest request,
        ClaimsPrincipal principal,
        HttpContext context,
        IApprovalService service,
        CancellationToken cancellationToken) =>
        DecideWithFixedActionAsync(
            approvalId, request, ApprovalDecisions.Reject, principal, context, service, cancellationToken);

    private static async Task<IResult> DecideWithFixedActionAsync(
        Guid approvalId,
        OwnerApprovalActionRequest request,
        string decision,
        ClaimsPrincipal principal,
        HttpContext context,
        IApprovalService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var approval = await service.GetAsync(approvalId, null, cancellationToken);
            if (approval is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Approval not found");
            }

            var result = await service.DecideAsync(new ApprovalDecisionCommand(
                approvalId,
                approval.OrganizationId,
                GetUserId(principal),
                decision,
                request.Payload,
                request.ExpectedVersion,
                request.Note,
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (ToolGatewayException exception)
        {
            return Problem(exception);
        }
    }

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
                "invalid_tool_request",
                $"{fieldName} must be a valid non-empty UUID.",
                400);

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated principal is missing a user identifier.");

    private static IResult Problem(ToolGatewayException exception) => Results.Problem(
        statusCode: exception.StatusCode,
        title: exception.Code,
        detail: exception.Message);
}
