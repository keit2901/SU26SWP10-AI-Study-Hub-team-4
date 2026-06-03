using System.Text;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag;

/// <summary>
/// Deterministic 384-dimensional feature-hashing embedder for demo/test use only.
/// It is offline and reproducible, but not a substitute for a trained embedding model.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    private readonly int _dimensions;

    public FakeEmbeddingService(IOptions<RagOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _dimensions = options.Value.EmbeddingDimensions;
        if (_dimensions != DocumentChunk.EmbeddingDimension)
        {
            throw new InvalidOperationException(
                $"Rag embedding dimension must remain {DocumentChunk.EmbeddingDimension} to match document_chunks.embedding.");
        }
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var embedding = new float[_dimensions];
        var tokenCount = 0;

        foreach (var token in Tokenize(text ?? string.Empty))
        {
            tokenCount++;
            var hash = StableHash(token);
            var index = (int)(hash % (uint)_dimensions);
            var sign = (hash & 0x8000_0000) == 0 ? 1f : -1f;
            var weight = 1f + MathF.Min(token.Length, 20) / 20f;
            embedding[index] += sign * weight;
        }

        if (tokenCount == 0)
        {
            embedding[0] = 1f;
        }
        else
        {
            NormalizeInPlace(embedding);
        }

        return Task.FromResult(embedding);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder(capacity: 32);

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                token.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (token.Length > 0)
            {
                yield return token.ToString();
                token.Clear();
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }

    private static uint StableHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }

    private static void NormalizeInPlace(float[] vector)
    {
        double sumSquares = 0;
        foreach (var value in vector)
        {
            sumSquares += value * value;
        }

        if (sumSquares <= 0)
        {
            vector[0] = 1f;
            return;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }
}
