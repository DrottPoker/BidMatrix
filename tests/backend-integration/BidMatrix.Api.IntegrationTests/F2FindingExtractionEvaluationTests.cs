using BidMatrix.Infrastructure.Analyses;

namespace BidMatrix.Api.IntegrationTests;

public sealed class F2FindingExtractionEvaluationTests
{
    [Fact]
    public void DetectorExtractsDatesDocumentsAndWeightedCriteriaWithExactSources()
    {
        var fileId = Guid.CreateVersion7();
        var pages = new[]
        {
            new RequirementSourcePage(
                fileId,
                "managed-security-rfp.pdf",
                4,
                "KEY DATES\nProposal submission deadline: September 30 2026.\nQuestions are due 2026-09-12."),
            new RequirementSourcePage(
                fileId,
                "managed-security-rfp.pdf",
                8,
                "SUBMISSION CONTENTS\nProposals shall include a valid ISO 27001 certificate."),
            new RequirementSourcePage(
                fileId,
                "managed-security-rfp.pdf",
                11,
                "EVALUATION\nTechnical solution evaluation weight: 60%.\nPrice weight: 40%."),
        };

        var findings = new DeterministicAnalysisFindingDetector().Detect(pages);

        var dates = findings.Where(item => item.FindingType == "key_date").ToArray();
        Assert.Equal(2, dates.Length);
        Assert.All(dates, item => Assert.NotNull(item.DateValue));
        Assert.Contains(dates, item => item.PageNumber == 4 && item.QuoteText.Contains("September 30 2026", StringComparison.Ordinal));

        var requestedDocument = Assert.Single(findings, item => item.FindingType == "requested_document");
        Assert.Equal("Certificate or certification", requestedDocument.Title);
        Assert.Equal(8, requestedDocument.PageNumber);
        Assert.Equal(requestedDocument.Detail, requestedDocument.QuoteText);

        var criteria = findings.Where(item => item.FindingType == "evaluation_criterion").ToArray();
        Assert.Equal(2, criteria.Length);
        Assert.Contains(criteria, item => item.Title == "Technical solution" && item.WeightPercent == 60m);
        Assert.Contains(criteria, item => item.Title == "Price" && item.WeightPercent == 40m);
        Assert.All(criteria, item => Assert.Equal(11, item.PageNumber));
    }
}
