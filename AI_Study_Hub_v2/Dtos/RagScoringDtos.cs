namespace AI_Study_Hub_v2.Dtos;

public sealed record RagScoringInfoResponse(
    int ChunkSizeChars,
    int ChunkOverlapChars,
    int EmbeddingDimensions,
    int DefaultTopK,
    int MaxTopK,
    string ScoreMeaning,
    string RankingMethod,
    string ChunkingStrategy = "fixed",
    int? MinChunkChars = null,
    int? MaxSectionChars = null);
