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
    string NormalizedRequirement,
    string Category,
    bool Mandatory,
    string? RequestedEvidence,
    decimal Confidence,
    string ReviewStatus,
    IReadOnlyList<AnalysisCitationRecord> Citations);

public sealed record AnalysisExtractionMetrics(
    int DocumentCount,
    int PageCount,
    int RequirementCount,
    int MandatoryRequirementCount,
    int CitedRequirementCount,
    int FilesRequiringOcr,
    int FailedFileCount);

public sealed record AnalysisExtractionSnapshot(
    Guid AnalysisId,
    string ExtractionStatus,
    string? ExtractionVersion,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<AnalysisDocumentRecord> Documents,
    IReadOnlyList<AnalysisRequirementRecord> Requirements,
    AnalysisExtractionMetrics Metrics);

public interface IAnalysisExtractionService
{
    Task<AnalysisExtractionSnapshot> ExtractAsync(
        ExtractAnalysisCommand command,
        CancellationToken cancellationToken = default);

    Task<AnalysisExtractionSnapshot?> GetAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken cancellationToken = default);
}
