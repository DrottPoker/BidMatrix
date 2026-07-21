using System.Text.RegularExpressions;

namespace BidMatrix.Infrastructure.Analyses;

public sealed record RequirementSourcePage(
    Guid AnalysisFileId,
    string OriginalFileName,
    int PageNumber,
    string Text);

public sealed record DetectedRequirement(
    string? RequirementCode,
    string RequirementText,
    string NormalizedRequirement,
    string Category,
    bool Mandatory,
    string? RequestedEvidence,
    decimal Confidence,
    Guid AnalysisFileId,
    string OriginalFileName,
    int PageNumber,
    string? SectionText,
    string QuoteText);

public interface IRequirementDetector
{
    string Version { get; }

    string Classify(IReadOnlyList<RequirementSourcePage> pages);

    IReadOnlyList<DetectedRequirement> Detect(IReadOnlyList<RequirementSourcePage> pages);
}

public sealed class DeterministicRequirementDetector : IRequirementDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MandatoryPattern = new(
        @"\b(must|shall|mandatory|required\s+to|is\s+required|are\s+required|no\s+later\s+than|will\s+be\s+rejected\s+if)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex StrongMandatoryPattern = new(
        @"\b(must|shall|mandatory|will\s+be\s+rejected\s+if)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex OptionalPattern = new(
        @"\b(should|preferred|desirable|may\s+provide)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex ImperativePattern = new(
        @"^(provide|submit|include|describe|demonstrate|confirm|identify|complete|attach)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex RequirementCodePattern = new(
        @"^\s*\[?(?<code>[A-Z]{2,}[A-Z0-9]*[-.]\d+(?:[-.]\d+)*)\]?\s*[:\-]?\s*",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex SentenceBoundaryPattern = new(
        @"(?<=[.!?;])\s+(?=[A-Z0-9\[])|\s*\|\s*",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex EvidencePattern = new(
        @"\b(provide|submit|include|attach|evidence|certificate|certification|reference|proof|report)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    public string Version => "rules-en-v1";

    public string Classify(IReadOnlyList<RequirementSourcePage> pages)
    {
        var content = string.Join('\n', pages.Select(page => page.Text)).ToLowerInvariant();
        if (content.Contains("invitation to tender", StringComparison.Ordinal) ||
            content.Contains("invitation to bid", StringComparison.Ordinal))
        {
            return "invitation_to_tender";
        }

        if (content.Contains("request for proposal", StringComparison.Ordinal) ||
            Regex.IsMatch(content, @"\brfp\b", RegexOptions.CultureInvariant, RegexTimeout))
        {
            return "request_for_proposal";
        }

        if (content.Contains("statement of work", StringComparison.Ordinal) ||
            Regex.IsMatch(content, @"\bsow\b", RegexOptions.CultureInvariant, RegexTimeout))
        {
            return "statement_of_work";
        }

        if (content.Contains("terms and conditions", StringComparison.Ordinal))
        {
            return "terms_and_conditions";
        }

        if (content.Contains("pricing schedule", StringComparison.Ordinal) ||
            content.Contains("price schedule", StringComparison.Ordinal))
        {
            return "pricing_schedule";
        }

        return "procurement_document";
    }

    public IReadOnlyList<DetectedRequirement> Detect(IReadOnlyList<RequirementSourcePage> pages)
    {
        var results = new List<DetectedRequirement>();
        foreach (var page in pages)
        {
            var sectionText = FindSectionText(page.Text);
            foreach (var statement in SplitStatements(page.Text))
            {
                if (statement.Length is < 12 or > 2_000)
                {
                    continue;
                }

                var codeMatch = RequirementCodePattern.Match(statement);
                var requirementCode = codeMatch.Success ? codeMatch.Groups["code"].Value : null;
                var requirementText = codeMatch.Success
                    ? statement[codeMatch.Length..].Trim()
                    : statement;
                var mandatory = MandatoryPattern.IsMatch(requirementText) ||
                    ImperativePattern.IsMatch(requirementText);
                var optional = OptionalPattern.IsMatch(requirementText);
                if (!mandatory && !optional)
                {
                    continue;
                }

                var confidence = StrongMandatoryPattern.IsMatch(requirementText)
                    ? 0.97m
                    : mandatory
                        ? 0.86m
                        : 0.72m;
                results.Add(new DetectedRequirement(
                    requirementCode,
                    requirementText,
                    NormalizeRequirement(requirementText),
                    ClassifyCategory(requirementText),
                    mandatory,
                    EvidencePattern.IsMatch(requirementText) ? requirementText : null,
                    confidence,
                    page.AnalysisFileId,
                    page.OriginalFileName,
                    page.PageNumber,
                    sectionText,
                    requirementText));
            }
        }

        return results;
    }

    public static string NormalizeRequirement(string value) => WhitespacePattern
        .Replace(value, " ")
        .Trim()
        .TrimEnd('.', ';', ':')
        .ToLowerInvariant();

    private static IEnumerable<string> SplitStatements(string text)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalizedLine = WhitespacePattern.Replace(line, " ").Trim();
            foreach (var statement in SentenceBoundaryPattern.Split(normalizedLine))
            {
                var normalizedStatement = statement.Trim();
                if (normalizedStatement.Length > 0)
                {
                    yield return normalizedStatement;
                }
            }
        }
    }

    private static string? FindSectionText(string text)
    {
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return firstLine is null ? null : firstLine[..Math.Min(firstLine.Length, 200)];
    }

    private static string ClassifyCategory(string text)
    {
        if (ContainsAny(text, "security", "encryption", "iso 27001", "soc 2", "privacy", "gdpr", "incident"))
        {
            return "security_compliance";
        }

        if (ContainsAny(text, "price", "pricing", "fee", "cost", "payment", "insurance", "liability", "contract"))
        {
            return "commercial_legal";
        }

        if (ContainsAny(text, "deadline", "no later than", "delivery", "implementation", "onboarding", "schedule"))
        {
            return "delivery_schedule";
        }

        if (ContainsAny(text, "experience", "reference", "qualification", "certification", "certificate", "staff"))
        {
            return "supplier_qualification";
        }

        if (ContainsAny(text, "technical", "api", "integration", "availability", "uptime", "support", "service"))
        {
            return "technical_service";
        }

        return "general";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}
