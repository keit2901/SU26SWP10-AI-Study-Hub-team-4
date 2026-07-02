using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class ChunkingServiceTests
{
    [Test]
    public void Chunk_ShortText_ReturnsSingleZeroBasedChunk()
    {
        var sut = BuildSemanticSut();
        var documentId = Guid.NewGuid();

        var chunks = sut.Chunk(documentId, new[] { new ExtractedPage(3, "Short paragraph for RAG.") });

        chunks.Should().ContainSingle();
        chunks[0].DocumentId.Should().Be(documentId);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].PageNumber.Should().Be(3);
        chunks[0].Content.Should().Be("Short paragraph for RAG.");
        chunks[0].SectionTitle.Should().BeNull();
    }

    [Test]
    public void Chunk_HeadingAndParagraph_StoresHeadingAsSectionTitleWithoutExtraChunk()
    {
        var sut = BuildSemanticSut();
        var documentId = Guid.NewGuid();
        var pageText = """
            CHUONG 1: GIOI THIEU

            AI la mot linh vuc lon. No giup may tinh hoc cach xu ly du lieu. Ung dung nay giup sinh vien on tap.
            """;

        var chunks = sut.Chunk(documentId, new[] { new ExtractedPage(1, pageText) });

        chunks.Should().ContainSingle();
        chunks[0].SectionTitle.Should().Be("CHUONG 1: GIOI THIEU");
        chunks[0].Content.Should().Contain("AI la mot linh vuc lon.");
    }

    [Test]
    public void Chunk_Paragraphs_UseSentenceOverlapBetweenParagraphs()
    {
        var sut = BuildSemanticSut(minChunkChars: 60, maxSectionChars: 400);
        var pageText = """
            Cau dau tien giai thich tong quan ve he thong. Cau thu hai bo sung them boi canh de doan dau du dai.

            Cau thu ba mo ta cach semantic chunking hoat dong. Cau thu tu noi ro loi ich cho truy van RAG.
            """;

        var chunks = sut.Chunk(Guid.NewGuid(), new[] { new ExtractedPage(1, pageText) });

        chunks.Should().HaveCount(2);
        chunks[1].Content.Should().StartWith("Cau thu hai bo sung them boi canh de doan dau du dai.");
        chunks[1].Content.Should().Contain("Cau thu ba mo ta cach semantic chunking hoat dong.");
    }

    [Test]
    public void Chunk_ListItems_MergesBulletsIntoSingleChunkWhenUnderLimit()
    {
        var sut = BuildSemanticSut();
        var pageText = """
            - Muc tieu thu nhat la giam noise.
            - Muc tieu thu hai la giu tron y nghia.
            - Muc tieu thu ba la ho tro truy van tot hon.
            """;

        var chunks = sut.Chunk(Guid.NewGuid(), new[] { new ExtractedPage(5, pageText) });

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Contain("Muc tieu thu nhat");
        chunks[0].Content.Should().Contain("Muc tieu thu ba");
    }

    [Test]
    public void FixedSizeChunkingService_LongText_SplitsWithOverlap()
    {
        var sut = BuildFixedSut(chunkSize: 20, overlap: 5);
        var text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        var chunks = sut.Chunk(Guid.NewGuid(), new[] { new ExtractedPage(1, text) });

        chunks.Count.Should().BeGreaterThan(1);
        chunks.Select(c => c.ChunkIndex).Should().Equal(Enumerable.Range(0, chunks.Count));
        chunks.All(c => c.Content.Length <= 20).Should().BeTrue();
        chunks[1].Content.Should().StartWith(chunks[0].Content[^5..].TrimStart());
    }

    private static ChunkingService BuildSemanticSut(int minChunkChars = 100, int maxSectionChars = 1000) =>
        new(
            new BlockParser(),
            new SentenceSplitter(),
            new ChunkMerger(Microsoft.Extensions.Options.Options.Create(new RagOptions
            {
                ChunkingStrategy = "semantic",
                MinChunkChars = minChunkChars,
                MaxSectionChars = maxSectionChars,
            })));

    private static FixedSizeChunkingService BuildFixedSut(int chunkSize, int overlap) =>
        new(Microsoft.Extensions.Options.Options.Create(new RagOptions
        {
            ChunkingStrategy = "fixed",
            ChunkSizeChars = chunkSize,
            ChunkOverlapChars = overlap,
        }));
}
