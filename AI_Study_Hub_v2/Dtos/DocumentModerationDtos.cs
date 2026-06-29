namespace AI_Study_Hub_v2.Dtos;

public sealed record ModerationQueueDocumentDto(
    Guid Id,
    string Title,
    string SubjectCode,
    string Semester,
    string OwnerName,
    string OwnerEmail,
    string FileType,
    long FileSizeBytes,
    string StoragePath,
    string ProcessingStatus,
    string ModerationStatus,
    string Severity,
    string ReportReason,
    string ModerationReason,
    string PreviewText,
    int ChunkCount,
    DateTimeOffset UploadedAt,
    DateTimeOffset UpdatedAt);
