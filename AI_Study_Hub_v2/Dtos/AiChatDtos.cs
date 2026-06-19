namespace AI_Study_Hub_v2.Dtos;

/// <summary>
/// Controls how the AI chat service retrieves context and generates answers.
/// </summary>
public enum AiChatMode
{
    /// <summary>Search indexed documents only. Refuse to answer if no sources match the query.</summary>
    RagOnly = 0,

    /// <summary>General knowledge mode. Skip document search entirely; answer using the model's own knowledge.</summary>
    GeneralKnowledge = 1,

    /// <summary>Search indexed documents first. If no relevant sources are found,
    /// fall back to a general knowledge answer with a disclaimer.</summary>
    RagWithFallback = 2,
}

public sealed record AiChatAskRequest(
    string Question,
    Guid? DocumentId,
    Guid? FolderId,
    string? SubjectCode,
    string? Semester,
    int TopK = 5,
    IReadOnlyList<Guid>? DocumentIds = null,
    AiChatMode Mode = AiChatMode.RagOnly);

public sealed record AiChatAnswerResponse(
    string Answer,
    IReadOnlyList<AiChatSourceDto> Sources,
    string? RefusalReason = null,
    long? DurationMs = null);

public sealed record AiChatSourceDto(
    string Label,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    string Excerpt,
    double? Score);
