using Pgvector;

namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// A single text chunk from a <see cref="Document"/>, with its embedding for vector search.
/// Chunk size + overlap follow plan L4 (1000 chars, 200 overlap, paragraph-aware when possible).
/// Embedding dimension = 384 (BGE-small-en-v1.5 ONNX, plan L2).
/// </summary>
public sealed class DocumentChunk
{
    /// <summary>Embedding dimension lock. Changing this requires a new migration + re-ingestion.</summary>
    public const int EmbeddingDimension = 384;

    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>0-based ordinal within the document.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>1-based page number containing this chunk; <c>null</c> if undetectable.</summary>
    public int? PageNumber { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>Rough estimate (~chars / 4) for prompt budget accounting.</summary>
    public int? TokenCount { get; set; }

    /// <summary>Cosine-similarity vector. Null only briefly during pipeline.</summary>
    public Vector Embedding { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public Document Document { get; set; } = null!;
}
