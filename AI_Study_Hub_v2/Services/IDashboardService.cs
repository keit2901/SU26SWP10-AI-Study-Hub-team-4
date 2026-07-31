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
    Task<UserAnalyticsDto> GetUserAnalyticsAsync(Guid userId, Guid? folderId = null, CancellationToken ct = default);
    Task<DocumentAiReviewResultDto?> AiReviewDocumentAsync(Guid supabaseUserId, Guid documentId, CancellationToken ct);
    Task<List<DocumentDto>> GetPendingModerationDocumentsAsync(Guid supabaseUserId, Guid? folderId, CancellationToken ct);
    Task ApproveDocumentAsync(Guid supabaseUserId, Guid documentId, CancellationToken ct);
    Task RejectDocumentAsync(Guid supabaseUserId, Guid documentId, string? reason, CancellationToken ct);
    Task<UserAnalyticsDto> GetModerationAnalyticsAsync(Guid supabaseUserId, Guid? folderId, int page, int pageSize, CancellationToken ct);
    Task<string?> GetModerationDocumentSignedUrlAsync(Guid supabaseUserId, Guid documentId, CancellationToken ct);

    /// <summary>
    /// Activity trends: document uploads/chats/failures by day, week, or month.
    /// </summary>
    Task<ActivityTrendsDto> GetActivityTrendsAsync(string period = "day", CancellationToken ct = default);
}
