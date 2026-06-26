using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Content;

namespace AI_Study_Hub_v2.Services.Rag;

public sealed class PdfTextExtractionService : ITextExtractionService
{
    private const string PdfMimeType = "application/pdf";
    private const string TextPlainMimeType = "text/plain";
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PptxMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    public async Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(mimeType, TextPlainMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractPlainTextAsync(fileStream, cancellationToken);
        }

        if (string.Equals(mimeType, PdfMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPdfPages(fileStream);
        }

        if (string.Equals(mimeType, DocxMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDocxPages(fileStream);
        }

        if (string.Equals(mimeType, PptxMimeType, StringComparison.OrdinalIgnoreCase))
        {
            return ExtractPptxPages(fileStream);
        }

        throw new NotSupportedException($"Text extraction for MIME type '{mimeType}' is not supported yet.");
    }

    private static IReadOnlyList<ExtractedPage> ExtractPdfPages(Stream fileStream)
    {
        var pages = new List<ExtractedPage>();
        var options = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
        };

        using var document = PdfDocument.Open(fileStream, options);
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            var images = ExtractPdfImages(page);
            pages.Add(new ExtractedPage(page.Number, text, images));
        }

        return pages;
    }

    private static IReadOnlyList<ExtractedImage> ExtractPdfImages(Page page)
    {
        var images = new List<ExtractedImage>();
        foreach (var pdfImage in page.GetImages())
        {
            if (pdfImage.TryGetPng(out var pngBytes))
            {
                images.Add(new ExtractedImage(pngBytes, "image/png"));
            }
            else
            {
                var rawBytes = pdfImage.RawBytes.ToArray();
                if (rawBytes.Length > 0)
                {
                    images.Add(new ExtractedImage(rawBytes, "image/jpeg"));
                }
            }
        }
        return images;
    }

    private static IReadOnlyList<ExtractedPage> ExtractDocxPages(Stream fileStream)
    {
        using var doc = WordprocessingDocument.Open(fileStream, false);
        var mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is null)
        {
            return Array.Empty<ExtractedPage>();
        }

        var text = new StringBuilder();
        var body = mainPart.Document.Body;

        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            text.AppendLine(para.InnerText);
        }

        if (mainPart.HeaderParts is not null)
        {
            foreach (var headerPart in mainPart.HeaderParts)
            {
                foreach (var para in headerPart.Header.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                {
                    text.AppendLine(para.InnerText);
                }
            }
        }

        if (mainPart.FooterParts is not null)
        {
            foreach (var footerPart in mainPart.FooterParts)
            {
                foreach (var para in footerPart.Footer.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                {
                    text.AppendLine(para.InnerText);
                }
            }
        }

        var fullText = text.ToString().Trim();
        var images = ExtractDocxImages(mainPart);
        return string.IsNullOrWhiteSpace(fullText)
            ? (images.Count > 0 ? new[] { new ExtractedPage(1, fullText, images) } : Array.Empty<ExtractedPage>())
            : new[] { new ExtractedPage(1, fullText, images) };
    }

    private static IReadOnlyList<ExtractedImage> ExtractDocxImages(MainDocumentPart mainPart)
    {
        var images = new List<ExtractedImage>();
        foreach (var imagePart in mainPart.ImageParts)
        {
            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var mimeType = imagePart.ContentType ?? "image/png";
            images.Add(new ExtractedImage(ms.ToArray(), mimeType));
        }
        return images;
    }

    private static IReadOnlyList<ExtractedPage> ExtractPptxPages(Stream fileStream)
    {
        using var pres = PresentationDocument.Open(fileStream, false);
        var slideParts = pres.PresentationPart?.SlideParts;
        if (slideParts is null || !slideParts.Any())
        {
            return Array.Empty<ExtractedPage>();
        }

        var pages = new List<ExtractedPage>();
        var pageNumber = 0;

        foreach (var slidePart in slideParts)
        {
            pageNumber++;
            var slide = slidePart.Slide;
            var text = new StringBuilder();

            foreach (var textBody in slide.Descendants<DocumentFormat.OpenXml.Drawing.TextBody>())
            {
                foreach (var para in textBody.Elements<DocumentFormat.OpenXml.Drawing.Paragraph>())
                {
                    text.AppendLine(para.InnerText);
                }
            }

            var slideText = text.ToString().Trim();
            var images = ExtractPptxImages(slidePart);
            pages.Add(new ExtractedPage(pageNumber, slideText, images));
        }

        return pages;
    }

    private static IReadOnlyList<ExtractedImage> ExtractPptxImages(SlidePart slidePart)
    {
        var images = new List<ExtractedImage>();
        foreach (var imagePart in slidePart.ImageParts)
        {
            using var stream = imagePart.GetStream();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var mimeType = imagePart.ContentType ?? "image/png";
            images.Add(new ExtractedImage(ms.ToArray(), mimeType));
        }
        return images;
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
