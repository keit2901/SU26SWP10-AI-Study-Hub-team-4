using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class ChunkMergerTests
{
    [Test]
    public void Merge_UsesHeadingAsSectionMetadata_WithoutStandaloneChunk()
    {
        var sut = BuildSut();
        var blocks = new[]
        {
            new SplitBlock(1, "CHUONG 1: GIOI THIEU", TextBlockKind.Heading, Array.Empty<string>()),
            new SplitBlock(1, "AI la mot linh vuc lon. No giup tim kiem tot hon.", TextBlockKind.Paragraph, new[]
            {
                "AI la mot linh vuc lon.",
                "No giup tim kiem tot hon."
            })
        };

        var chunks = sut.Merge(blocks);

        chunks.Should().ContainSingle();
        chunks[0].IsHeading.Should().BeFalse();
        chunks[0].SectionTitle.Should().Be("CHUONG 1: GIOI THIEU");
    }

    [Test]
    public void Merge_AddsParagraphOverlapToNextParagraph()
    {
        var sut = BuildSut(minChunkChars: 40, maxSectionChars: 400);
        var blocks = new[]
        {
            new SplitBlock(1, "Doan 1", TextBlockKind.Paragraph, new[]
            {
                "Cau mot mo ta boi canh.",
                "Cau hai ket noi voi doan sau."
            }),
            new SplitBlock(1, "Doan 2", TextBlockKind.Paragraph, new[]
            {
                "Cau ba mo rong y tuong.",
                "Cau bon ket luan."
            })
        };

        var chunks = sut.Merge(blocks);

        chunks.Should().HaveCount(2);
        chunks[1].Content.Should().StartWith("Cau hai ket noi voi doan sau.");
    }

    [Test]
    public void Merge_KeepsSectionBoundaryButAddsPreviousSectionOverlap()
    {
        var sut = BuildSut(minChunkChars: 80, maxSectionChars: 400);
        var blocks = new[]
        {
            new SplitBlock(1, "SECTION 1", TextBlockKind.Heading, Array.Empty<string>()),
            new SplitBlock(1, "Doan ngan", TextBlockKind.Paragraph, new[]
            {
                "Tom tat ngan cho section mot."
            }),
            new SplitBlock(2, "SECTION 2", TextBlockKind.Heading, Array.Empty<string>()),
            new SplitBlock(2, "Doan dai", TextBlockKind.Paragraph, new[]
            {
                "Doan cua section hai giai thich ky hon ve retrieval va chunking.",
                "No can duoc giu rieng de khong bi merge sai section."
            })
        };

        var chunks = sut.Merge(blocks);

        chunks.Should().HaveCount(2);
        chunks[0].SectionTitle.Should().Be("SECTION 1");
        chunks[1].SectionTitle.Should().Be("SECTION 2");
        chunks[1].Content.Should().StartWith("Tom tat ngan cho section mot.");
        chunks[1].Content.Should().Contain("Doan cua section hai giai thich ky hon ve retrieval va chunking.");
    }

    private static ChunkMerger BuildSut(int minChunkChars = 100, int maxSectionChars = 1000) =>
        new(Microsoft.Extensions.Options.Options.Create(new RagOptions
        {
            MinChunkChars = minChunkChars,
            MaxSectionChars = maxSectionChars,
        }));
}
