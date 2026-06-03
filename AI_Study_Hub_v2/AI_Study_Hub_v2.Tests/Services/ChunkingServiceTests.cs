using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class ChunkingServiceTests
{
    [Test]
    public void Chunk_ShortText_ReturnsSingleZeroBasedChunk()
    {
        var sut = BuildSut(chunkSize: 1000, overlap: 200);
        var documentId = Guid.NewGuid();

        var chunks = sut.Chunk(documentId, new[] { new ExtractedPage(3, "Short paragraph for RAG.") });

        chunks.Should().ContainSingle();
        chunks[0].DocumentId.Should().Be(documentId);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].PageNumber.Should().Be(3);
        chunks[0].Content.Should().Be("Short paragraph for RAG.");
    }

    [Test]
    public void Chunk_LongText_SplitsWithOverlap()
    {
        var sut = BuildSut(chunkSize: 20, overlap: 5);
        var text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        var chunks = sut.Chunk(Guid.NewGuid(), new[] { new ExtractedPage(1, text) });

        chunks.Count.Should().BeGreaterThan(1);
        chunks.Select(c => c.ChunkIndex).Should().Equal(Enumerable.Range(0, chunks.Count));
        chunks.All(c => c.Content.Length <= 20).Should().BeTrue();
        chunks[1].Content.Should().StartWith(chunks[0].Content[^5..].TrimStart());
    }

    [Test]
    public void Chunk_EmptyPages_SkipsThemAndKeepsPageNumbers()
    {
        var sut = BuildSut(chunkSize: 1000, overlap: 200);

        var chunks = sut.Chunk(Guid.NewGuid(), new[]
        {
            new ExtractedPage(1, "   \r\n\t"),
            new ExtractedPage(2, "Useful text"),
            new ExtractedPage(3, string.Empty),
        });

        chunks.Should().ContainSingle();
        chunks[0].PageNumber.Should().Be(2);
        chunks[0].Content.Should().Be("Useful text");
    }

    private static ChunkingService BuildSut(int chunkSize, int overlap) =>
        new(Microsoft.Extensions.Options.Options.Create(new RagOptions
        {
            ChunkSizeChars = chunkSize,
            ChunkOverlapChars = overlap,
        }));
}
