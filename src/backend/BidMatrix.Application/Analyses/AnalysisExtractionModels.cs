namespace BidMatrix.Application.Analyses;

public sealed record ExtractAnalysisCommand(
    Guid OrganizationId,
    Guid AnalysisId,
    string CorrelationId);

public sealed record AnalysisDocumentRecord(
    Guid AnalysisFileId,
    string OriginalFileName,
    string ExtractionStatus,
    string? DocumentType,
    int? PageCount,
    string? ExtractionMethod,
    string? FailureCode);

public sealed record AnalysisCitationRecord(
    Guid Id,
    Guid AnalysisFileId,
    string OriginalFileName,
    int PageNumber,
    string? SectionText,
    string QuoteText);

public sealed record AnalysisRequirementRecord(
    Guid Id,
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
    IReadOnlyList<AnalysisCitationRecord> Citations);

public sealed record AnalysisFindingRecord(
    Guid Id,
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
    AnalysisCitationRecord Citation);

public sealed record AnalysisPublicationRecord(
    string AnalysisStatus,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? PublishedAt,
    string? ReviewNote,
    int CorrectionCount,
    long? ProcessingDurationMilliseconds,
    bool IsPublished);

public sealed record AnalysisExtractionMetrics(
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

public sealed record AnalysisExtractionSnapshot(
    Guid AnalysisId,
    string ExtractionStatus,
    string? ExtractionVersion,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<AnalysisDocumentRecord> Documents,
    IReadOnlyList<AnalysisRequirementRecord> Requirements,
    IReadOnlyList<AnalysisFindingRecord> Findings,
    AnalysisPublicationRecord Publication,
    AnalysisExtractionMetrics Metrics);

public sealed record ReviewRequirementCommand(
    Guid OrganizationId,
    Guid OwnerUserId,
    Guid AnalysisId,
    Guid RequirementId,
    string RequirementText,
    string Category,
    bool Mandatory,
    string ReviewStatus,
    string? CorrectionNote,
    int ExpectedVersion,
    string CorrelationId);

public sealed record ReviewFindingCommand(
    Guid OrganizationId,
    Guid OwnerUserId,
    Guid AnalysisId,
    Guid FindingId,
    string Title,
    string Detail,
    DateTimeOffset? DateValue,
    decimal? WeightPercent,
    string ReviewStatus,
    string? CorrectionNote,
    int ExpectedVersion,
    string CorrelationId);

public sealed record PublishAnalysisCommand(
    Guid OrganizationId,
    Guid OwnerUserId,
    Guid AnalysisId,
    string ReviewNote,
    string Confirmation,
    string CorrelationId);

public interface IAnalysisExtractionService
{
    Task<AnalysisExtractionSnapshot> ExtractAsync(
        ExtractAnalysisCommand command,
        CancellationToken cancellationToken = default);

    Task<AnalysisExtractionSnapshot?> GetAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default);

    Task<AnalysisExtractionSnapshot> ReviewRequirementAsync(
        ReviewRequirementCommand command,
        CancellationToken cancellationToken = default);

    Task<AnalysisExtractionSnapshot> ReviewFindingAsync(
        ReviewFindingCommand command,
        CancellationToken cancellationToken = default);

    Task<AnalysisExtractionSnapshot> PublishAsync(
        PublishAnalysisCommand command,
        CancellationToken cancellationToken = default);
}
