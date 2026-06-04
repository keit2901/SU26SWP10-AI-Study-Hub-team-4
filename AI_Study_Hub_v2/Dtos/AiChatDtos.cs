namespace AI_Study_Hub_v2.Dtos;

public sealed record AiChatAskRequest(
    string Question,
    Guid? DocumentId,
    Guid? FolderId,
    string? SubjectCode,
    string? Semester,
    int TopK = 5,
    IReadOnlyList<Guid>? DocumentIds = null);

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
