namespace AI_Study_Hub_v2.Dtos;

public sealed record RagSearchRequest(
    string Query,
    Guid? DocumentId,
    Guid? FolderId,
    string? SubjectCode,
    string? Semester,
    int TopK = 5);

public sealed record RagSearchResultDto(
    string SourceLabel,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    string ContentExcerpt,
    double? Score);
