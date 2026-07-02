using System.Security.Cryptography;
using System.Text;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

/// <summary>
/// Small in-memory cache for repeated query embeddings during chat and search flows.
/// </summary>
public sealed class CachingEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly IMemoryCache _cache;
    private readonly RagOptions _options;
    private readonly ILogger<CachingEmbeddingService> _logger;

    public CachingEmbeddingService(
        IEmbeddingService inner,
        IMemoryCache cache,
        IOptions<RagOptions> options,
        ILogger<CachingEmbeddingService> logger)
    {
        _inner = inner;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!_options.EmbeddingCacheEnabled)
        {
            return await _inner.GenerateEmbeddingAsync(text, cancellationToken);
        }

        var normalized = text?.Trim() ?? string.Empty;
        var cacheKey = "rag:embedding:" + ComputeSha256(normalized);

        if (_cache.TryGetValue<float[]>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug("Embedding cache hit: hash={HashPrefix}", cacheKey[^12..]);
            return cached;
        }

        var embedding = await _inner.GenerateEmbeddingAsync(normalized, cancellationToken);
        var ttlMinutes = Math.Max(1, _options.EmbeddingCacheTtlMinutes);
        var maxEntries = Math.Max(1, _options.EmbeddingCacheMaxEntries);

        _cache.Set(
            cacheKey,
            embedding,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes),
                Size = 1,
                Priority = CacheItemPriority.Normal
            });

        _logger.LogDebug(
            "Embedding cache miss stored: hash={HashPrefix}, ttlMinutes={TtlMinutes}, maxEntries={MaxEntries}",
            cacheKey[^12..],
            ttlMinutes,
            maxEntries);

        return embedding;
    }

    private static string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
