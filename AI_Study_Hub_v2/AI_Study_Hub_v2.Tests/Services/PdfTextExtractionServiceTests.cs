using AI_Study_Hub_v2.Services.Rag;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

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
