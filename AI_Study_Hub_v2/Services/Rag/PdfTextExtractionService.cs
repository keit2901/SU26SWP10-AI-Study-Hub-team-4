using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Content;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

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

        var blocks = new List<ExtractedBlock>();
        CollectWordBlocks(mainPart.Document.Body, blocks);

        if (mainPart.HeaderParts is not null)
        {
            foreach (var headerPart in mainPart.HeaderParts)
            {
                if (headerPart.Header is not null)
                {
                    CollectWordBlocks(headerPart.Header, blocks);
                }
            }
        }

        if (mainPart.FooterParts is not null)
        {
            foreach (var footerPart in mainPart.FooterParts)
            {
                if (footerPart.Footer is not null)
                {
                    CollectWordBlocks(footerPart.Footer, blocks);
                }
            }
        }

        var fullText = RenderBlocks(blocks);
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

    private static void CollectWordBlocks(OpenXmlElement container, ICollection<ExtractedBlock> blocks)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case W.Paragraph paragraph:
                    blocks.Add(new ExtractedBlock(ExtractWordParagraph(paragraph), false));
                    break;
                case W.Table table:
                    blocks.Add(new ExtractedBlock(ExtractWordTable(table), true));
                    break;
                case W.SdtBlock sdtBlock when sdtBlock.SdtContentBlock is not null:
                    CollectWordBlocks(sdtBlock.SdtContentBlock, blocks);
                    break;
            }
        }
    }

    private static string ExtractWordTable(W.Table table) => string.Join('\n', table.Elements<W.TableRow>()
        .Select(row => string.Join('\t', row.Elements<W.TableCell>().Select(ExtractWordCell))));

    private static string ExtractWordCell(W.TableCell cell)
    {
        var blocks = new List<ExtractedBlock>();
        CollectWordBlocks(cell, blocks);
        return RenderBlocks(blocks);
    }

    private static string ExtractWordParagraph(W.Paragraph paragraph)
    {
        var text = ExtractWordText(paragraph);
        var numbering = paragraph.ParagraphProperties?.NumberingProperties;
        if (numbering is null)
        {
            return text;
        }

        var level = numbering.NumberingLevelReference?.Val?.Value ?? 0;
        return string.Concat(new string(' ', Math.Max(0, level) * 2), "- ", text);
    }

    private static string ExtractWordText(OpenXmlElement element)
    {
        var text = new StringBuilder();
        foreach (var child in element.Descendants())
        {
            switch (child)
            {
                case W.Text value:
                    text.Append(value.Text);
                    break;
                case W.TabChar:
                    text.Append('\t');
                    break;
                case W.Break:
                case W.CarriageReturn:
                    text.Append('\n');
                    break;
            }
        }

        return text.ToString();
    }

    private static IReadOnlyList<ExtractedPage> ExtractPptxPages(Stream fileStream)
    {
        using var pres = PresentationDocument.Open(fileStream, false);
        var presentationPart = pres.PresentationPart;
        var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<P.SlideId>().ToList();
        if (presentationPart is null || slideIds is null || slideIds.Count == 0)
        {
            return Array.Empty<ExtractedPage>();
        }

        var pages = new List<ExtractedPage>();
        var pageNumber = 0;

        foreach (var slideId in slideIds)
        {
            var relationshipId = slideId.RelationshipId?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId) || presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                continue;
            }

            pageNumber++;
            var blocks = new List<ExtractedBlock>();
            var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;
            if (shapeTree is not null)
            {
                CollectPresentationBlocks(shapeTree, blocks);
            }

            var slideText = RenderBlocks(blocks);
            var images = ExtractPptxImages(slidePart);
            pages.Add(new ExtractedPage(pageNumber, slideText, images));
        }

        return pages;
    }

    private static void CollectPresentationBlocks(OpenXmlElement container, ICollection<ExtractedBlock> blocks)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case P.Shape shape when shape.TextBody is not null:
                    foreach (var paragraph in shape.TextBody.Elements<A.Paragraph>())
                    {
                        blocks.Add(new ExtractedBlock(ExtractPresentationParagraph(paragraph), false));
                    }
                    break;
                case P.GroupShape groupShape:
                    CollectPresentationBlocks(groupShape, blocks);
                    break;
                case P.GraphicFrame graphicFrame:
                    var table = graphicFrame.Graphic?.GraphicData?.GetFirstChild<A.Table>();
                    if (table is not null)
                    {
                        blocks.Add(new ExtractedBlock(ExtractPresentationTable(table), true));
                    }
                    break;
            }
        }
    }

    private static string ExtractPresentationTable(A.Table table) => string.Join('\n', table.Elements<A.TableRow>()
        .Select(row => string.Join('\t', row.Elements<A.TableCell>().Select(ExtractPresentationCell))));

    private static string ExtractPresentationCell(A.TableCell cell) => cell.TextBody is null
        ? string.Empty
        : string.Join('\n', cell.TextBody.Elements<A.Paragraph>().Select(ExtractPresentationParagraph));

    private static string ExtractPresentationParagraph(A.Paragraph paragraph)
    {
        var text = paragraph.InnerText;
        var level = paragraph.ParagraphProperties?.Level?.Value;
        return level is null
            ? text
            : string.Concat(new string(' ', Math.Max(0, level.Value) * 2), "- ", text);
    }

    private static string RenderBlocks(IEnumerable<ExtractedBlock> blocks)
    {
        var rendered = new StringBuilder();
        ExtractedBlock? previous = null;
        foreach (var block in blocks)
        {
            if (previous is not null)
            {
                rendered.Append(previous.Value.IsTable || block.IsTable ? "\n\n" : "\n");
            }

            rendered.Append(block.Text);
            previous = block;
        }

        return rendered.ToString().Trim('\r', '\n');
    }

    private readonly record struct ExtractedBlock(string Text, bool IsTable);

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
