using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IAiAnswerReportService
{
    Task<AiAnswerReportResponse> ReportAsync(
        Guid supabaseUserId,
        AiAnswerReportRequest request,
        CancellationToken cancellationToken = default);
}
