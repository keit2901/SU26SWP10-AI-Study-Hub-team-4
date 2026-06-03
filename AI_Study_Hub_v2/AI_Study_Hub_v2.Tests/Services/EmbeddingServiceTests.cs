using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class EmbeddingServiceTests
{
    [Test]
    public async Task GenerateEmbeddingAsync_Returns384Dimensions()
    {
        var sut = new FakeEmbeddingService(OptionsFactory.Create(new RagOptions()));

        var embedding = await sut.GenerateEmbeddingAsync("semantic search over lecture notes");

        embedding.Should().HaveCount(384);
    }

    [Test]
    public async Task GenerateEmbeddingAsync_IsDeterministic_ForSameInput()
    {
        var sut = new FakeEmbeddingService(OptionsFactory.Create(new RagOptions()));

        var first = await sut.GenerateEmbeddingAsync("RAG retrieves grounded source chunks.");
        var second = await sut.GenerateEmbeddingAsync("RAG retrieves grounded source chunks.");

        second.Should().Equal(first);
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ReturnsNormalizedVector_ForNonEmptyInput()
    {
        var sut = new FakeEmbeddingService(OptionsFactory.Create(new RagOptions()));

        var embedding = await sut.GenerateEmbeddingAsync("normalization keeps cosine distance stable");
        var magnitude = Math.Sqrt(embedding.Sum(value => value * value));

        magnitude.Should().BeApproximately(1d, 0.0001d);
    }
}
