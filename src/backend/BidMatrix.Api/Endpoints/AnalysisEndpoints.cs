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

        var owner = endpoints.MapGroup("/owner/v1/analyses")
            .RequireAuthorization(BidMatrixPolicies.PlatformOwner)
            .WithTags("Owner");
        owner.MapGet("", ListAnalysesAsync)
            .WithName("ListOwnerAnalyses");
        owner.MapGet("/{analysisId:guid}/review", GetOwnerReviewAsync)
            .WithName("GetOwnerAnalysisReview");
        owner.MapPatch("/{analysisId:guid}/requirements/{requirementId:guid}", ReviewRequirementAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("ReviewAnalysisRequirement");
        owner.MapPatch("/{analysisId:guid}/findings/{findingId:guid}", ReviewFindingAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("ReviewAnalysisFinding");
        owner.MapPost("/{analysisId:guid}/publish", PublishAnalysisAsync)
            .AddEndpointFilter<ValidateAntiforgeryFilter>()
            .WithName("PublishReviewedAnalysis");

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
        internalRoutes.MapPost("/analyses/{analysisId:guid}/intake/extract", ExtractAnalysisAsync)
            .WithName("ExtractAnalysisDocuments");
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
        IAnalysisExtractionService service,
        CancellationToken cancellationToken)
    {
        var extraction = await service.GetAsync(GetOrganizationId(principal), analysisId, cancellationToken);
        return extraction is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Analysis not found")
            : Results.Ok(ToResponse(extraction, publishedOnly: true));
    }

    private static async Task<IResult> GetOwnerReviewAsync(
        Guid analysisId,
        ClaimsPrincipal principal,
        IAnalysisExtractionService service,
        CancellationToken cancellationToken)
    {
        var extraction = await service.GetAsync(GetOrganizationId(principal), analysisId, cancellationToken);
        return extraction is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Analysis not found")
            : Results.Ok(ToResponse(extraction));
    }

    private static async Task<IResult> ReviewRequirementAsync(
        Guid analysisId,
        Guid requirementId,
        [FromBody] ReviewRequirementRequest request,
        HttpContext context,
        IAnalysisExtractionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ReviewRequirementAsync(new ReviewRequirementCommand(
                GetOrganizationId(context.User),
                GetUserId(context.User),
                analysisId,
                requirementId,
                request.RequirementText,
                request.Category,
                request.Mandatory,
                request.ReviewStatus,
                request.CorrectionNote,
                request.ExpectedVersion,
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> ReviewFindingAsync(
        Guid analysisId,
        Guid findingId,
        [FromBody] ReviewFindingRequest request,
        HttpContext context,
        IAnalysisExtractionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ReviewFindingAsync(new ReviewFindingCommand(
                GetOrganizationId(context.User),
                GetUserId(context.User),
                analysisId,
                findingId,
                request.Title,
                request.Detail,
                request.DateValue,
                request.WeightPercent,
                request.ReviewStatus,
                request.CorrectionNote,
                request.ExpectedVersion,
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
    }

    private static async Task<IResult> PublishAnalysisAsync(
        Guid analysisId,
        [FromBody] PublishAnalysisRequest request,
        HttpContext context,
        IAnalysisExtractionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.PublishAsync(new PublishAnalysisCommand(
                GetOrganizationId(context.User),
                GetUserId(context.User),
                analysisId,
                request.ReviewNote,
                request.Confirmation,
                context.TraceIdentifier), cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (AnalysisOperationException exception)
        {
            return Problem(exception);
        }
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

    private static async Task<IResult> ExtractAnalysisAsync(
        Guid analysisId,
        [FromBody] AnalysisIntakeRequest request,
        IAnalysisExtractionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var extraction = await service.ExtractAsync(new ExtractAnalysisCommand(
                ParseOrganizationId(request.OrganizationId),
                analysisId,
                request.CorrelationId), cancellationToken);
            return Results.Ok(ToResponse(extraction));
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

    private static AnalysisRequirementsResponse ToResponse(
        AnalysisExtractionSnapshot extraction,
        bool publishedOnly = false)
    {
        var capabilityStatus = extraction.Publication.IsPublished
            ? "ready"
            : extraction.ExtractionStatus is "succeeded" or "partial"
                ? "qualityReview"
            : extraction.ExtractionStatus == "failed"
                ? "failed"
                : "notReady";
        var message = extraction.Publication.IsPublished
            ? "This analysis was quality reviewed and published by BidMatrix. Every item remains linked to its source."
            : extraction.ExtractionStatus switch
        {
            "succeeded" => "Extraction is complete and is being quality reviewed before delivery.",
            "partial" => "Extraction is in quality review. One or more files may require OCR or manual handling.",
            "failed" => "Document extraction failed. No unverified requirements were fabricated.",
            "processing" => "Digital PDF extraction is processing.",
            _ => "Digital PDF extraction has not started.",
        };
        var showResults = !publishedOnly || extraction.Publication.IsPublished;
        var requirements = showResults
            ? extraction.Requirements.Where(item => item.ReviewStatus != "rejected").ToArray()
            : [];
        var findings = showResults
            ? extraction.Findings.Where(item => item.ReviewStatus != "rejected").ToArray()
            : [];
        return new AnalysisRequirementsResponse(
            extraction.AnalysisId.ToString(),
            capabilityStatus,
            extraction.ExtractionStatus,
            extraction.ExtractionVersion,
            extraction.CompletedAt,
            extraction.Documents.Select(document => new AnalysisDocumentResponse(
                document.AnalysisFileId.ToString(),
                document.OriginalFileName,
                document.ExtractionStatus,
                document.DocumentType,
                document.PageCount,
                document.ExtractionMethod,
                document.FailureCode)).ToArray(),
            requirements.Select(requirement => new AnalysisRequirementResponse(
                requirement.Id.ToString(),
                requirement.RequirementCode,
                requirement.RequirementText,
                requirement.OriginalRequirementText,
                requirement.NormalizedRequirement,
                requirement.Category,
                requirement.Mandatory,
                requirement.RequestedEvidence,
                requirement.Confidence,
                requirement.ReviewStatus,
                requirement.CorrectionNote,
                requirement.Version,
                requirement.Citations.Select(citation => new AnalysisCitationResponse(
                    citation.Id.ToString(),
                    citation.AnalysisFileId.ToString(),
                    citation.OriginalFileName,
                    citation.PageNumber,
                    citation.SectionText,
                    citation.QuoteText)).ToArray())).ToArray(),
            findings.Where(item => item.FindingType == "key_date").Select(ToResponse).ToArray(),
            findings.Where(item => item.FindingType == "requested_document").Select(ToResponse).ToArray(),
            findings.Where(item => item.FindingType == "evaluation_criterion").Select(ToResponse).ToArray(),
            new AnalysisPublicationResponse(
                extraction.Publication.AnalysisStatus,
                extraction.Publication.ReviewedAt,
                extraction.Publication.PublishedAt,
                extraction.Publication.ReviewNote,
                extraction.Publication.CorrectionCount,
                extraction.Publication.ProcessingDurationMilliseconds,
                extraction.Publication.IsPublished),
            new AnalysisExtractionMetricsResponse(
                extraction.Metrics.DocumentCount,
                extraction.Metrics.PageCount,
                extraction.Metrics.RequirementCount,
                extraction.Metrics.MandatoryRequirementCount,
                extraction.Metrics.CitedRequirementCount,
                extraction.Metrics.KeyDateCount,
                extraction.Metrics.RequestedDocumentCount,
                extraction.Metrics.EvaluationCriterionCount,
                extraction.Metrics.PendingReviewCount,
                extraction.Metrics.FilesRequiringOcr,
                extraction.Metrics.FailedFileCount),
            message);
    }

    private static AnalysisFindingResponse ToResponse(AnalysisFindingRecord finding) => new(
        finding.Id.ToString(),
        finding.FindingType,
        finding.Title,
        finding.Detail,
        finding.OriginalDetail,
        finding.DateValue,
        finding.WeightPercent,
        finding.Confidence,
        finding.ReviewStatus,
        finding.CorrectionNote,
        finding.Version,
        new AnalysisCitationResponse(
            finding.Citation.Id.ToString(),
            finding.Citation.AnalysisFileId.ToString(),
            finding.Citation.OriginalFileName,
            finding.Citation.PageNumber,
            finding.Citation.SectionText,
            finding.Citation.QuoteText));
}
