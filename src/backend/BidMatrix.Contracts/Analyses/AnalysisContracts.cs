namespace BidMatrix.Contracts.Analyses;

public sealed record CreateAnalysisRequest(string? Title);

public sealed record AnalysisFileResponse(
    string Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string ScanStatus,
    DateTimeOffset? RetentionUntil,
    DateTimeOffset CreatedAt);

public sealed record AnalysisResponse(
    string Id,
    string? Title,
    string Status,
    string SourceLanguage,
    string? WorkflowId,
    bool RequiresHumanReview,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    IReadOnlyList<AnalysisFileResponse> Files);

public sealed record AnalysisListResponse(IReadOnlyList<AnalysisResponse> Analyses);

public sealed record AnalysisFileUploadResponse(AnalysisFileResponse File, bool Duplicate);

public sealed record AnalysisDocumentResponse(
    string AnalysisFileId,
    string OriginalFileName,
    string ExtractionStatus,
    string? DocumentType,
    int? PageCount,
    string? ExtractionMethod,
    string? FailureCode);

public sealed record AnalysisCitationResponse(
    string Id,
    string AnalysisFileId,
    string OriginalFileName,
    int PageNumber,
    string? SectionText,
    string QuoteText);

public sealed record AnalysisRequirementResponse(
    string Id,
    string? RequirementCode,
    string RequirementText,
    string OriginalRequirementText,
    string NormalizedRequirement,
    string Category,
    bool Mandatory,
    string? RequestedEvidence,
    decimal Confidence,
    string ReviewStatus,
    string? CorrectionNote,
    int Version,
    IReadOnlyList<AnalysisCitationResponse> Citations);

public sealed record AnalysisFindingResponse(
    string Id,
    string FindingType,
    string Title,
    string Detail,
    string OriginalDetail,
    DateTimeOffset? DateValue,
    decimal? WeightPercent,
    decimal Confidence,
    string ReviewStatus,
    string? CorrectionNote,
    int Version,
    AnalysisCitationResponse Citation);

public sealed record AnalysisPublicationResponse(
    string AnalysisStatus,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? PublishedAt,
    string? ReviewNote,
    int CorrectionCount,
    long? ProcessingDurationMilliseconds,
    bool IsPublished);

public sealed record AnalysisExtractionMetricsResponse(
    int DocumentCount,
    int PageCount,
    int RequirementCount,
    int MandatoryRequirementCount,
    int CitedRequirementCount,
    int KeyDateCount,
    int RequestedDocumentCount,
    int EvaluationCriterionCount,
    int PendingReviewCount,
    int FilesRequiringOcr,
    int FailedFileCount);

public sealed record AnalysisRequirementsResponse(
    string AnalysisId,
    string CapabilityStatus,
    string ExtractionStatus,
    string? ExtractionVersion,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<AnalysisDocumentResponse> Documents,
    IReadOnlyList<AnalysisRequirementResponse> Requirements,
    IReadOnlyList<AnalysisFindingResponse> KeyDates,
    IReadOnlyList<AnalysisFindingResponse> RequestedDocuments,
    IReadOnlyList<AnalysisFindingResponse> EvaluationCriteria,
    AnalysisPublicationResponse Publication,
    AnalysisExtractionMetricsResponse Metrics,
    string Message);

public sealed record ReviewRequirementRequest(
    string RequirementText,
    string Category,
    bool Mandatory,
    string ReviewStatus,
    string? CorrectionNote,
    int ExpectedVersion);

public sealed record ReviewFindingRequest(
    string Title,
    string Detail,
    DateTimeOffset? DateValue,
    decimal? WeightPercent,
    string ReviewStatus,
    string? CorrectionNote,
    int ExpectedVersion);

public sealed record PublishAnalysisRequest(
    string ReviewNote,
    string Confirmation);

public sealed record ClaimedEventResponse(
    string EventId,
    string EventType,
    string AggregateId,
    string Payload,
    int AttemptCount,
    DateTimeOffset LeaseExpiresAt);

public sealed record ClaimEventsResponse(IReadOnlyList<ClaimedEventResponse> Events);

public sealed record FailEventRequest(string Error);

public sealed record AnalysisIntakeRequest(string OrganizationId, string CorrelationId);

public sealed record AnalysisIntakeStateResponse(
    string AnalysisId,
    string OrganizationId,
    string Status,
    IReadOnlyList<string> FileScanStatuses);

public sealed record ManualReviewTaskResponse(string TaskId);
