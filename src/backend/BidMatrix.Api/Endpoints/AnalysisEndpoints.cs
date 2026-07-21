using System.Security.Claims;
using BidMatrix.Api.Security;
using BidMatrix.Application.Analyses;
using BidMatrix.Application.Workflows;
using BidMatrix.Contracts.Analyses;
using BidMatrix.Infrastructure.Analyses;
using BidMatrix.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BidMatrix.Api.Endpoints;

public static class AnalysisEndpoints
{
    public static IEndpointRouteBuilder MapBidMatrixAnalysisEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/v1/analyses")
            .RequireAuthorization(BidMatrixPolicies.Customer)
            .WithTags("Analyses");

        customer.MapPost("", CreateAnalysisAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("CreateAnalysis");
        customer.MapGet("", ListAnalysesAsync)
            .WithName("ListAnalyses");
        customer.MapGet("/{analysisId:guid}", GetAnalysisAsync)
            .WithName("GetAnalysis");
        customer.MapPost("/{analysisId:guid}/files", UploadFileAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("UploadAnalysisFile");
        customer.MapPost("/{analysisId:guid}/submit", SubmitAnalysisAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("SubmitAnalysis");
        customer.MapPost("/{analysisId:guid}/cancel", CancelAnalysisAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("CancelAnalysis");
        customer.MapGet("/{analysisId:guid}/requirements", GetRequirementsAsync)
            .WithName("GetAnalysisRequirements");

        endpoints.MapGet("/owner/v1/analyses", ListAnalysesAsync)
            .RequireAuthorization(BidMatrixPolicies.PlatformOwner)
            .WithTags("Owner")
            .WithName("ListOwnerAnalyses");

        var internalRoutes = endpoints.MapGroup("/internal/v1")
            .RequireAuthorization(BidMatrixPolicies.InternalService)
            .WithTags("Internal");
        internalRoutes.MapGet("/events/claim", ClaimEventsAsync)
            .WithName("ClaimOutboxEvents");
        internalRoutes.MapPost("/events/{eventId:guid}/ack", AcknowledgeEventAsync)
            .WithName("AcknowledgeOutboxEvent");
        internalRoutes.MapPost("/events/{eventId:guid}/fail", FailEventAsync)
            .WithName("FailOutboxEvent");
        internalRoutes.MapGet("/analyses/{analysisId:guid}/intake", GetAnalysisIntakeAsync)
            .WithName("GetAnalysisIntake");
        internalRoutes.MapPost("/analyses/{analysisId:guid}/intake/processing", MarkProcessingAsync)
            .WithName("MarkAnalysisProcessing");
        internalRoutes.MapPost("/analyses/{analysisId:guid}/intake/manual-review-task", CreateManualReviewTaskAsync)
            .WithName("CreateAnalysisManualReviewTask");
        internalRoutes.MapPost("/analyses/{analysisId:guid}/intake/requires-review", MarkRequiresReviewAsync)
            .WithName("MarkAnalysisRequiresReview");

        return endpoints;
    }

    private static async Task<IResult> CreateAnalysisAsync(
        [FromBody] CreateAnalysisRequest request,
        HttpContext context,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyValues))
        {
            return Problem(new AnalysisOperationException(
                "idempotency_key_required",
                "Idempotency-Key is required when creating an analysis.",
                StatusCodes.Status400BadRequest));
        }

        try
        {
            var analysis = await service.CreateAsync(
                new CreateAnalysisCommand(
                    GetOrganizationId(context.User),
                    GetUserId(context.User),
                    request.Title,
                    idempotencyValues.ToString(),
                    context.TraceIdentifier),
                cancellationToken);
            return Results.Created($"/v1/analyses/{analysis.Id}", ToResponse(analysis));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ListAnalysesAsync(
        ClaimsPrincipal principal,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        var analyses = await service.ListAsync(GetOrganizationId(principal), cancellationToken);
        return Results.Ok(new AnalysisListResponse(analyses.Select(ToResponse).ToArray()));
    }

    private static async Task<IResult> GetAnalysisAsync(
        Guid analysisId,
        ClaimsPrincipal principal,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        var analysis = await service.GetAsync(GetOrganizationId(principal), analysisId, cancellationToken);
        return analysis is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Analysis not found")
            : Results.Ok(ToResponse(analysis));
    }

    private static async Task<IResult> UploadFileAsync(
        Guid analysisId,
        IFormFile file,
        HttpContext context,
        AnalysisOptions options,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        try
        {
            if (file.Length > options.MaxPdfUploadBytes)
            {
                throw new AnalysisOperationException(
                    "file_too_large",
                    $"The PDF exceeds the configured {options.MaxPdfUploadBytes}-byte limit.",
                    StatusCodes.Status413PayloadTooLarge);
            }

            await using var buffer = new MemoryStream((int)Math.Max(file.Length, 0));
            await file.CopyToAsync(buffer, cancellationToken);
            var result = await service.UploadFileAsync(
                new UploadAnalysisFileCommand(
                    GetOrganizationId(context.User),
                    GetUserId(context.User),
                    analysisId,
                    file.FileName,
                    file.ContentType,
                    buffer.ToArray(),
                    context.TraceIdentifier),
                cancellationToken);
            return Results.Ok(new AnalysisFileUploadResponse(ToResponse(result.File), result.Duplicate));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> SubmitAnalysisAsync(
        Guid analysisId,
        HttpContext context,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await service.SubmitAsync(
                GetOrganizationId(context.User),
                GetUserId(context.User),
                analysisId,
                context.TraceIdentifier,
                cancellationToken);
            return Results.Ok(ToResponse(analysis));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> CancelAnalysisAsync(
        Guid analysisId,
        HttpContext context,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await service.CancelAsync(
                GetOrganizationId(context.User),
                GetUserId(context.User),
                analysisId,
                context.TraceIdentifier,
                cancellationToken);
            return Results.Ok(ToResponse(analysis));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> GetRequirementsAsync(
        Guid analysisId,
        ClaimsPrincipal principal,
        IAnalysisService service,
        CancellationToken cancellationToken)
    {
        var analysis = await service.GetAsync(GetOrganizationId(principal), analysisId, cancellationToken);
        return analysis is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Analysis not found")
            : Results.Ok(new AnalysisRequirementsResponse(
                analysisId.ToString(),
                "notImplemented",
                [],
                "Automated requirement extraction is not available in Foundation Release F0."));
    }

    private static async Task<IResult> ClaimEventsAsync(
        [FromQuery] string eventType,
        [FromQuery] int limit,
        ClaimsPrincipal principal,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var workerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Internal principal is missing a worker identifier.");
            var events = await service.ClaimAsync(workerId, eventType, limit, cancellationToken);
            return Results.Ok(new ClaimEventsResponse(events.Select(item => new ClaimedEventResponse(
                item.EventId.ToString(),
                item.EventType,
                item.AggregateId.ToString(),
                item.Payload,
                item.AttemptCount,
                item.LeaseExpiresAt)).ToArray()));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> AcknowledgeEventAsync(
        Guid eventId,
        ClaimsPrincipal principal,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken)
    {
        var workerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Internal principal is missing a worker identifier.");
        return await service.AcknowledgeAsync(eventId, workerId, cancellationToken)
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Event lease is not owned by this worker");
    }

    private static async Task<IResult> FailEventAsync(
        Guid eventId,
        [FromBody] FailEventRequest request,
        ClaimsPrincipal principal,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken)
    {
        var workerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Internal principal is missing a worker identifier.");
        return await service.FailAsync(eventId, workerId, request.Error, cancellationToken)
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Event lease is not owned by this worker");
    }

    private static async Task<IResult> GetAnalysisIntakeAsync(
        Guid analysisId,
        [FromQuery] Guid organizationId,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken)
    {
        var state = await service.GetAnalysisIntakeAsync(organizationId, analysisId, cancellationToken);
        return state is null
            ? Results.NotFound()
            : Results.Ok(new AnalysisIntakeStateResponse(
                state.AnalysisId.ToString(),
                state.OrganizationId.ToString(),
                state.Status,
                state.FileScanStatuses));
    }

    private static async Task<IResult> MarkProcessingAsync(
        Guid analysisId,
        [FromBody] AnalysisIntakeRequest request,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken) => await RunIntakeMutationAsync(
        request,
        (organizationId, correlationId) => service.MarkAnalysisProcessingAsync(
            organizationId,
            analysisId,
            correlationId,
            cancellationToken));

    private static async Task<IResult> CreateManualReviewTaskAsync(
        Guid analysisId,
        [FromBody] AnalysisIntakeRequest request,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var taskId = await service.CreateManualReviewTaskAsync(
                ParseOrganizationId(request.OrganizationId),
                analysisId,
                request.CorrelationId,
                cancellationToken);
            return Results.Ok(new ManualReviewTaskResponse(taskId.ToString()));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> MarkRequiresReviewAsync(
        Guid analysisId,
        [FromBody] AnalysisIntakeRequest request,
        IWorkflowBridgeService service,
        CancellationToken cancellationToken) => await RunIntakeMutationAsync(
        request,
        (organizationId, correlationId) => service.MarkAnalysisRequiresReviewAsync(
            organizationId,
            analysisId,
            correlationId,
            cancellationToken));

    private static async Task<IResult> RunIntakeMutationAsync(
        AnalysisIntakeRequest request,
        Func<Guid, string, Task> operation)
    {
        try
        {
            await operation(ParseOrganizationId(request.OrganizationId), request.CorrelationId);
            return Results.NoContent();
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static Guid GetOrganizationId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(BidMatrixClaimTypes.OrganizationId), out var organizationId)
            ? organizationId
            : throw new InvalidOperationException("Authenticated principal is missing organization context.");

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new InvalidOperationException("Authenticated principal is missing a user identifier.");

    private static Guid ParseOrganizationId(string organizationId) =>
        Guid.TryParse(organizationId, out var parsed)
            ? parsed
            : throw new AnalysisOperationException(
                "invalid_organization_id",
                "A valid organization ID is required.",
                StatusCodes.Status400BadRequest);

    private static IResult Problem(AnalysisOperationException exception) => Results.Problem(
        statusCode: exception.StatusCode,
        title: exception.Code,
        detail: exception.Message);

    private static AnalysisResponse ToResponse(AnalysisRecord analysis) => new(
        analysis.Id.ToString(),
        analysis.Title,
        analysis.Status,
        analysis.SourceLanguage,
        analysis.WorkflowId,
        analysis.RequiresHumanReview,
        analysis.FailureCode,
        analysis.FailureMessage,
        analysis.CreatedAt,
        analysis.UpdatedAt,
        analysis.Version,
        analysis.Files.Select(ToResponse).ToArray());

    private static AnalysisFileResponse ToResponse(AnalysisFileRecord file) => new(
        file.Id.ToString(),
        file.OriginalFileName,
        file.ContentType,
        file.SizeBytes,
        file.Sha256,
        file.ScanStatus,
        file.RetentionUntil,
        file.CreatedAt);
}
