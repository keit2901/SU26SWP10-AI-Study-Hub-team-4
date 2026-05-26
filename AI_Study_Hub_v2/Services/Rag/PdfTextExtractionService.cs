using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class PdfTextExtractionService : ITextExtractionService
{
    private const string PdfMimeType = "application/pdf";
    private const string TextPlainMimeType = "text/plain";

    public Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(mimeType, TextPlainMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPlainTextAsync(fileStream, cancellationToken);
        }

        if (!string.Equals(mimeType, PdfMimeType, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Text extraction for MIME type '{mimeType}' is not supported yet.");
        }

        var pages = new List<ExtractedPage>();
        var options = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
        };

        using var document = PdfDocument.Open(fileStream, options);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ContentOrderTextExtractor.GetText(page);
            pages.Add(new ExtractedPage(page.Number, text));
        }

        return Task.FromResult<IReadOnlyList<ExtractedPage>>(pages);
    }

    private static async Task<IReadOnlyList<ExtractedPage>> ExtractPlainTextAsync(
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(fileStream, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new[] { new ExtractedPage(1, text) };
    }
}
