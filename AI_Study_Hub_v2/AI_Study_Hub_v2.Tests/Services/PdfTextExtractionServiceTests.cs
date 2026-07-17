using AI_Study_Hub_v2.Services.Rag;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class PdfTextExtractionServiceTests
{
    [Test]
    public async Task ExtractPagesAsync_Pdf_ReturnsPageAwareText()
    {
        var sut = new PdfTextExtractionService();
        using var stream = new MemoryStream(BuildPdf(
            "Sprint two ingestion text",
            "Second page citation text"));

        var pages = await sut.ExtractPagesAsync(stream, "application/pdf");

        pages.Should().HaveCount(2);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("Sprint two ingestion text");
        pages[1].PageNumber.Should().Be(2);
        pages[1].Text.Should().Contain("Second page citation text");
    }

    [Test]
    public async Task ExtractPagesAsync_UnsupportedMime_ThrowsNotSupported()
    {
        var sut = new PdfTextExtractionService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var act = () => sut.ExtractPagesAsync(stream, "image/jpeg");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Test]
    public async Task ExtractPagesAsync_Docx_PreservesParagraphListTableAndTrailingParagraphOrder()
    {
        using var stream = BuildDocx(new W.Paragraph(new W.Run(new W.Text("Intro"))),
            ListParagraph("First", 0),
            ListParagraph("Nested", 1),
            Table(("A", "B"), ("C", "D")),
            new W.Paragraph(new W.Run(new W.Text("After"))));

        var pages = await new PdfTextExtractionService().ExtractPagesAsync(stream, DocxMimeType);

        pages.Should().ContainSingle();
        pages[0].Text.Should().Be("Intro\n- First\n  - Nested\n\nA\tB\nC\tD\n\nAfter");
    }

    [Test]
    public async Task ExtractPagesAsync_Docx_PreservesMultiParagraphAndEmptyCellsWithoutDuplicateText()
    {
        using var stream = BuildDocx(Table(("First\nSecond", "")));

        var pages = await new PdfTextExtractionService().ExtractPagesAsync(stream, DocxMimeType);

        pages.Should().ContainSingle();
        pages[0].Text.Should().Be("First\nSecond\t");
        pages[0].Text.Split("First", StringSplitOptions.None).Length.Should().Be(2);
    }

    [Test]
    public async Task ExtractPagesAsync_Docx_PreservesSdtBlockContentInDocumentOrder()
    {
        using var stream = BuildDocx(
            new W.Paragraph(new W.Run(new W.Text("Before"))),
            new W.SdtBlock(new W.SdtContentBlock(new W.Paragraph(new W.Run(new W.Text("Controlled"))))),
            new W.Paragraph(new W.Run(new W.Text("After"))));

        var pages = await new PdfTextExtractionService().ExtractPagesAsync(stream, DocxMimeType);

        pages.Should().ContainSingle();
        pages[0].Text.Should().Be("Before\nControlled\nAfter");
    }

    [Test]
    public async Task ExtractPagesAsync_Docx_PreservesNestedTablesInsideCells()
    {
        var nested = Table(("Nested", "Value"));
        var outer = new W.Table(new W.TableRow(
            new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Outer"))), nested),
            new W.TableCell(new W.Paragraph())));
        using var stream = BuildDocx(outer);

        var pages = await new PdfTextExtractionService().ExtractPagesAsync(stream, DocxMimeType);

        pages.Should().ContainSingle();
        pages[0].Text.Should().Be("Outer\n\nNested\tValue\t");
    }

    [Test]
    public async Task ExtractPagesAsync_Pptx_UsesSlideIdListOrderAndPreservesShapeAndTableOrder()
    {
        using var stream = BuildPptx();

        var pages = await new PdfTextExtractionService().ExtractPagesAsync(stream, PptxMimeType);

        pages.Should().HaveCount(2);
        pages.Select(page => page.PageNumber).Should().Equal(1, 2);
        pages[0].Text.Should().Be("First slide");
        pages[1].Text.Should().Be("Title\n- Bullet\n\nX\tY");
        pages[1].Text.Split("X", StringSplitOptions.None).Length.Should().Be(2);
    }

    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PptxMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static MemoryStream BuildDocx(params OpenXmlElement[] blocks)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body(blocks));
        }

        stream.Position = 0;
        return stream;
    }

    private static W.Paragraph ListParagraph(string text, int level) => new(
        new W.ParagraphProperties(new W.NumberingProperties(
            new W.NumberingLevelReference { Val = level },
            new W.NumberingId { Val = 1 })),
        new W.Run(new W.Text(text)));

    private static W.Table Table(params (string Left, string Right)[] rows) => new(
        rows.Select(row => new W.TableRow(
            new W.TableCell(Paragraphs(row.Left)),
            new W.TableCell(Paragraphs(row.Right)))));

    private static W.Paragraph[] Paragraphs(string text) => text.Split('\n').Select(line =>
        new W.Paragraph(new W.Run(new W.Text(line)))).ToArray();

    private static MemoryStream BuildPptx()
    {
        var stream = new MemoryStream();
        using (var presentation = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = presentation.AddPresentationPart();
            var firstCreated = AddSlide(presentationPart, "Created first", null);
            var secondCreated = AddSlide(presentationPart, "First slide", null);
            var orderedLast = AddSlide(presentationPart, "Title", "Bullet");
            orderedLast.Slide.CommonSlideData!.ShapeTree!.Append(TableFrame("X", "Y"));

            presentationPart.Presentation = new P.Presentation(
                new P.SlideIdList(
                    new P.SlideId { Id = 256U, RelationshipId = presentationPart.GetIdOfPart(secondCreated) },
                    new P.SlideId { Id = 257U, RelationshipId = presentationPart.GetIdOfPart(orderedLast) }));
        }

        stream.Position = 0;
        return stream;
    }

    private static SlidePart AddSlide(PresentationPart presentationPart, string title, string? bullet)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        var shapeTree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(),
            TextShape(title, false));
        if (bullet is not null)
        {
            shapeTree.Append(TextShape(bullet, true));
        }

        slidePart.Slide = new P.Slide(new P.CommonSlideData(shapeTree), new P.ColorMapOverride(new A.MasterColorMapping()));
        return slidePart;
    }

    private static P.Shape TextShape(string text, bool bullet)
    {
        var paragraph = new A.Paragraph(new A.Run(new A.Text(text)));
        if (bullet)
        {
            paragraph.PrependChild(new A.ParagraphProperties { Level = 0 });
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = 2U, Name = string.Empty },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(),
            new P.TextBody(new A.BodyProperties(), new A.ListStyle(), paragraph));
    }

    private static P.GraphicFrame TableFrame(string left, string right) => new(
        new P.NonVisualGraphicFrameProperties(
            new P.NonVisualDrawingProperties { Id = 3U, Name = string.Empty },
            new P.NonVisualGraphicFrameDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.Transform(new A.Offset(), new A.Extents()),
        new A.Graphic(new A.GraphicData(
            new A.Table(new A.TableRow(
                new A.TableCell(new A.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(left))))),
                new A.TableCell(new A.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(right))))))))
        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" }));

    private static byte[] BuildPdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var pageText in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(pageText, 12, new PdfPoint(50, 750), font);
        }

        return builder.Build();
    }
}
