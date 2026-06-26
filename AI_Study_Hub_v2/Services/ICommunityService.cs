using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface ICommunityService
{
    Task<Guid> ReportFolderAsync(
        Guid reportedByUserId,
        Guid folderId,
        string reason,
        CancellationToken ct = default);

    Task<IReadOnlyList<CommunityReportDto>> GetPendingReportsAsync(
        CancellationToken ct = default);

    Task ResolveReportAsync(
        Guid resolvedByUserId,
        Guid reportId,
        string status,
        string? resolution,
        CancellationToken ct = default);
}
