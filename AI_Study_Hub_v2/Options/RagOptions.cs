namespace AI_Study_Hub_v2.Options;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public int ChunkSizeChars { get; set; } = 700;

    public int ChunkOverlapChars { get; set; } = 200;

    public int DefaultTopK { get; set; } = 5;

    public int MaxTopK { get; set; } = 50;

    public int EmbeddingDimensions { get; set; } = 384;

    public int MaxContextChars { get; set; } = 6000;
}
