using System;
using System.Threading;
using System.Threading.Tasks;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IDashboardService
{
    Task<AdminDashboardStatsDto> GetAdminStatsAsync(CancellationToken ct = default);
    Task<UserDashboardStatsDto> GetUserStatsAsync(Guid userId, CancellationToken ct = default);
    Task<List<DashboardSubjectDto>> GetSubjectsStatsAsync(CancellationToken ct = default);
    Task<List<DashboardSemesterDto>> GetSemestersStatsAsync(CancellationToken ct = default);
    Task<List<DocumentDto>> GetPendingDocumentsAsync(Guid? folderId = null, CancellationToken ct = default);
    Task<bool> ApproveDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> RejectDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<UserAnalyticsDto> GetUserAnalyticsAsync(Guid userId, Guid? folderId = null, CancellationToken ct = default);

    /// <summary>
    /// Generate a signed download URL for a document (admin/moderator bypass —
    /// no ownership check). Returns null if document not found.
    /// </summary>
    Task<string?> GetDocumentSignedUrlAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Admin/moderator analytics — aggregates data across all pending-share folders
    /// (or for a specific folder when folderId is set). Does not scope to a single user.
    /// Supports server-side pagination with page/pageSize.
    /// </summary>
    Task<UserAnalyticsDto> GetAdminAnalyticsAsync(Guid? folderId = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>
    /// Activity trends: document uploads/chats/failures by day, week, or month.
    /// </summary>
    Task<ActivityTrendsDto> GetActivityTrendsAsync(string period = "day", CancellationToken ct = default);
}
