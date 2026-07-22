using System.Globalization;
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

public sealed record DetectedFinding(
    string FindingType,
    string Title,
    string Detail,
    string NormalizedValue,
    DateTimeOffset? DateValue,
    decimal? WeightPercent,
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

public interface IAnalysisFindingDetector
{
    string Version { get; }

    IReadOnlyList<DetectedFinding> Detect(IReadOnlyList<RequirementSourcePage> pages);
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
            var sectionText = AnalysisTextRules.FindSectionText(page.Text);
            foreach (var statement in AnalysisTextRules.SplitStatements(page.Text))
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

    public static string NormalizeRequirement(string value) => AnalysisTextRules.Normalize(value);

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

public sealed class DeterministicAnalysisFindingDetector : IAnalysisFindingDetector
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex DatePattern = new(
        @"\b(?<date>(?:20\d{2}[-/.](?:0?[1-9]|1[0-2])[-/.](?:0?[1-9]|[12]\d|3[01]))|(?:(?:0?[1-9]|[12]\d|3[01])\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+20\d{2})|(?:(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+(?:0?[1-9]|[12]\d|3[01])(?:st|nd|rd|th)?,?\s+20\d{2}))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex DateContextPattern = new(
        @"\b(deadline|due|submission|submit|questions?|clarifications?|award|contract|commencement|start|kickoff|presentation|validity|milestone|response)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex RequestedDocumentPattern = new(
        @"\b(provide|submit|include|attach|complete|upload)\b.*\b(certificate|certification|reference|case study|proposal|schedule|plan|report|matrix|declaration|form|insurance|evidence|cv|resume|pricing|price)\b|\b(certificate|certification|reference|case study|proposal|schedule|plan|report|matrix|declaration|form|insurance|evidence|cv|resume|pricing|price)\b.*\b(required|requested|must|shall)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex EvaluationPattern = new(
        @"\b(evaluation|criterion|criteria|weight|weighted|score|scoring|points?|quality|price|technical|methodology|presentation)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WeightPattern = new(
        @"(?<weight>100(?:\.0+)?|\d{1,2}(?:\.\d+)?)\s*%",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    public string Version => "findings-en-v1";

    public IReadOnlyList<DetectedFinding> Detect(IReadOnlyList<RequirementSourcePage> pages)
    {
        var findings = new List<DetectedFinding>();
        foreach (var page in pages)
        {
            var sectionText = AnalysisTextRules.FindSectionText(page.Text);
            foreach (var statement in AnalysisTextRules.SplitStatements(page.Text))
            {
                if (statement.Length is < 8 or > 2_000)
                {
                    continue;
                }

                var dateMatch = DatePattern.Match(statement);
                if (dateMatch.Success && DateContextPattern.IsMatch(statement))
                {
                    var dateText = dateMatch.Groups["date"].Value;
                    findings.Add(Create(
                        "key_date",
                        ClassifyDateTitle(statement),
                        statement,
                        TryParseDate(dateText),
                        null,
                        0.91m,
                        page,
                        sectionText));
                }

                if (RequestedDocumentPattern.IsMatch(statement))
                {
                    findings.Add(Create(
                        "requested_document",
                        ClassifyDocumentTitle(statement),
                        statement,
                        null,
                        null,
                        0.88m,
                        page,
                        sectionText));
                }

                var weightMatch = WeightPattern.Match(statement);
                if (weightMatch.Success && EvaluationPattern.IsMatch(statement))
                {
                    findings.Add(Create(
                        "evaluation_criterion",
                        ClassifyEvaluationTitle(statement),
                        statement,
                        null,
                        decimal.Parse(weightMatch.Groups["weight"].Value, CultureInfo.InvariantCulture),
                        0.94m,
                        page,
                        sectionText));
                }
            }
        }

        return findings
            .DistinctBy(finding => (finding.FindingType, finding.NormalizedValue, finding.AnalysisFileId, finding.PageNumber))
            .ToArray();
    }

    private static DetectedFinding Create(
        string findingType,
        string title,
        string detail,
        DateTimeOffset? dateValue,
        decimal? weightPercent,
        decimal confidence,
        RequirementSourcePage page,
        string? sectionText) => new(
            findingType,
            title,
            detail,
            AnalysisTextRules.Normalize(detail),
            dateValue,
            weightPercent,
            confidence,
            page.AnalysisFileId,
            page.OriginalFileName,
            page.PageNumber,
            sectionText,
            detail);

    private static DateTimeOffset? TryParseDate(string value)
    {
        var normalized = Regex.Replace(value, @"(?<=\d)(st|nd|rd|th)\b", string.Empty, RegexOptions.IgnoreCase, RegexTimeout);
        return DateTimeOffset.TryParse(
            normalized,
            CultureInfo.GetCultureInfo("en-US"),
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var date)
            ? date
            : null;
    }

    private static string ClassifyDateTitle(string value)
    {
        if (value.Contains("question", StringComparison.OrdinalIgnoreCase) || value.Contains("clarification", StringComparison.OrdinalIgnoreCase)) return "Questions deadline";
        if (value.Contains("submission", StringComparison.OrdinalIgnoreCase) || value.Contains("proposal", StringComparison.OrdinalIgnoreCase) || value.Contains("response", StringComparison.OrdinalIgnoreCase)) return "Proposal deadline";
        if (value.Contains("award", StringComparison.OrdinalIgnoreCase)) return "Expected award";
        if (value.Contains("commencement", StringComparison.OrdinalIgnoreCase) || value.Contains("kickoff", StringComparison.OrdinalIgnoreCase) || value.Contains("start", StringComparison.OrdinalIgnoreCase)) return "Contract start";
        return "Key procurement date";
    }

    private static string ClassifyDocumentTitle(string value)
    {
        if (value.Contains("certificate", StringComparison.OrdinalIgnoreCase) || value.Contains("certification", StringComparison.OrdinalIgnoreCase)) return "Certificate or certification";
        if (value.Contains("reference", StringComparison.OrdinalIgnoreCase) || value.Contains("case study", StringComparison.OrdinalIgnoreCase)) return "Customer reference";
        if (value.Contains("pricing", StringComparison.OrdinalIgnoreCase) || value.Contains("price", StringComparison.OrdinalIgnoreCase)) return "Pricing response";
        if (value.Contains("plan", StringComparison.OrdinalIgnoreCase)) return "Delivery plan";
        if (value.Contains("matrix", StringComparison.OrdinalIgnoreCase)) return "Compliance matrix";
        return "Requested submission document";
    }

    private static string ClassifyEvaluationTitle(string value)
    {
        if (value.Contains("price", StringComparison.OrdinalIgnoreCase)) return "Price";
        if (value.Contains("technical", StringComparison.OrdinalIgnoreCase)) return "Technical solution";
        if (value.Contains("methodology", StringComparison.OrdinalIgnoreCase) || value.Contains("quality", StringComparison.OrdinalIgnoreCase)) return "Quality and methodology";
        if (value.Contains("presentation", StringComparison.OrdinalIgnoreCase)) return "Presentation";
        return "Evaluation criterion";
    }
}

internal static class AnalysisTextRules
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex SentenceBoundaryPattern = new(
        @"(?<=[.!?;])\s+(?=[A-Z0-9\[])|\s*\|\s*",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    public static IEnumerable<string> SplitStatements(string text)
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

    public static string? FindSectionText(string text)
    {
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return firstLine is null ? null : firstLine[..Math.Min(firstLine.Length, 200)];
    }

    public static string Normalize(string value) => WhitespacePattern
        .Replace(value, " ")
        .Trim()
        .TrimEnd('.', ';', ':')
        .ToLowerInvariant();
}
