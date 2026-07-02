using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class CachingEmbeddingServiceTests
{
    [Test]
    public async Task GenerateEmbeddingAsync_ReusesCachedValueForSameQuery()
    {
        var inner = new Mock<IEmbeddingService>();
        inner.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1f, 2f, 3f });

        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var sut = new CachingEmbeddingService(
            inner.Object,
            cache,
            OptionsFactory.Create(new RagOptions
            {
                EmbeddingCacheEnabled = true,
                EmbeddingCacheMaxEntries = 10,
                EmbeddingCacheTtlMinutes = 30
            }),
            NullLogger<CachingEmbeddingService>.Instance);

        var first = await sut.GenerateEmbeddingAsync("same query");
        var second = await sut.GenerateEmbeddingAsync("same query");

        first.Should().Equal(second);
        inner.Verify(x => x.GenerateEmbeddingAsync("same query", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GenerateEmbeddingAsync_BypassesCacheWhenDisabled()
    {
        var inner = new Mock<IEmbeddingService>();
        inner.SetupSequence(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1f, 2f, 3f })
            .ReturnsAsync(new[] { 4f, 5f, 6f });

        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var sut = new CachingEmbeddingService(
            inner.Object,
            cache,
            OptionsFactory.Create(new RagOptions
            {
                EmbeddingCacheEnabled = false
            }),
            NullLogger<CachingEmbeddingService>.Instance);

        var first = await sut.GenerateEmbeddingAsync("same query");
        var second = await sut.GenerateEmbeddingAsync("same query");

        first.Should().NotEqual(second);
        inner.Verify(x => x.GenerateEmbeddingAsync("same query", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
