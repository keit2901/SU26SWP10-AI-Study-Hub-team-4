namespace AI_Study_Hub_v2.Dtos;

public sealed record CreateReportRequest(Guid FolderId, string Reason);

public sealed record ResolveReportRequest(string Status, string? Resolution = null);

public sealed record CommunityReportDto(
    Guid Id,
    Guid FolderId,
    string FolderName,
    Guid ReportedByUserId,
    string ReportedByName,
    string Reason,
    string Status,
    string? Resolution,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);
