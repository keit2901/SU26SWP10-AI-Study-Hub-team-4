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
    Task<List<DocumentDto>> GetPendingDocumentsAsync(CancellationToken ct = default);
    Task<bool> ApproveDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> RejectDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<UserAnalyticsDto> GetUserAnalyticsAsync(Guid userId, CancellationToken ct = default);
}
