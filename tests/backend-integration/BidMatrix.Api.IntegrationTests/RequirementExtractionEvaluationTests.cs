using System.Text.Json;
using BidMatrix.Infrastructure.Analyses;

namespace BidMatrix.Api.IntegrationTests;

public sealed class RequirementExtractionEvaluationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void F1EvaluationSetHasCompleteMandatoryRecallAndPageCitations()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "f1-evaluation-set.json");
        var evaluation = JsonSerializer.Deserialize<EvaluationSet>(File.ReadAllText(fixturePath), JsonOptions)
            ?? throw new InvalidOperationException("F1 evaluation fixture could not be loaded.");
        var detector = new DeterministicRequirementDetector();
        var matched = 0;
        var expected = 0;

        foreach (var document in evaluation.Documents)
        {
            var fileId = Guid.CreateVersion7();
            var pages = document.Pages.Select((text, index) => new RequirementSourcePage(
                fileId,
                $"{document.Id}.pdf",
                index + 1,
                text)).ToArray();
            Assert.Equal(document.DocumentType, detector.Classify(pages));

            var mandatory = detector.Detect(pages).Where(requirement => requirement.Mandatory).ToArray();
            Assert.Equal(document.ExpectedMandatoryRequirements.Count, mandatory.Length);
            foreach (var expectedRequirement in document.ExpectedMandatoryRequirements)
            {
                expected++;
                var result = Assert.Single(mandatory, requirement =>
                    requirement.NormalizedRequirement == expectedRequirement.NormalizedRequirement &&
                    requirement.PageNumber == expectedRequirement.PageNumber);
                Assert.Equal(result.RequirementText, result.QuoteText);
                matched++;
            }
        }

        Assert.Equal(5, expected);
        Assert.Equal(expected, matched);
        Assert.Equal(1m, decimal.Divide(matched, expected));
    }

    private sealed record EvaluationSet(string Version, IReadOnlyList<EvaluationDocument> Documents);

    private sealed record EvaluationDocument(
        string Id,
        string DocumentType,
        IReadOnlyList<string> Pages,
        IReadOnlyList<ExpectedRequirement> ExpectedMandatoryRequirements);

    private sealed record ExpectedRequirement(string NormalizedRequirement, int PageNumber);
}
