using System.Security.Cryptography;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace BidMatrix.Infrastructure.Analyses;

public sealed record ExtractedTextPage(int PageNumber, string Text, string TextSha256);

public sealed record ExtractedPdfDocument(IReadOnlyList<ExtractedTextPage> Pages);

public interface IDocumentTextExtractor
{
    string Version { get; }

    ExtractedPdfDocument Extract(ReadOnlyMemory<byte> content);
}

public sealed class PdfPigDocumentTextExtractor : IDocumentTextExtractor
{
    private const int MaximumPages = 500;
    private const int MaximumExtractedCharacters = 2_000_000;

    public string Version => "pdfpig-0.1.15";

    public ExtractedPdfDocument Extract(ReadOnlyMemory<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        using var document = PdfDocument.Open(stream);
        if (document.NumberOfPages > MaximumPages)
        {
            throw new InvalidDataException($"The PDF exceeds the {MaximumPages}-page extraction limit.");
        }

        var pages = new List<ExtractedTextPage>(document.NumberOfPages);
        var totalCharacters = 0;
        foreach (var page in document.GetPages())
        {
            var text = NormalizePageText(ContentOrderTextExtractor.GetText(page));
            totalCharacters += text.Length;
            if (totalCharacters > MaximumExtractedCharacters)
            {
                throw new InvalidDataException(
                    $"The PDF exceeds the {MaximumExtractedCharacters}-character extraction limit.");
            }

            pages.Add(new ExtractedTextPage(
                page.Number,
                text,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))));
        }

        return new ExtractedPdfDocument(pages);
    }

    private static string NormalizePageText(string value)
    {
        var normalizedLines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => string.Join(' ', line.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .ToArray();
        return string.Join('\n', normalizedLines).Trim();
    }
}
