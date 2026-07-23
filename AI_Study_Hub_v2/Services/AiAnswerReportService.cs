using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class AiAnswerReportService : IAiAnswerReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;

    public AiAnswerReportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AiAnswerReportResponse> ReportAsync(
        Guid supabaseUserId,
        AiAnswerReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await ResolveActiveUserAsync(supabaseUserId, cancellationToken);
        var question = NormalizeRequired(request.Question, "question_required", "Question is required.");
        var answer = NormalizeRequired(request.Answer, "answer_required", "Answer is required.");
        var reason = NormalizeRequired(request.Reason, "reason_required", "Report reason is required.");

        if (reason.Length > 80)
        {
            throw new AiStudyFeatureException(400, "reason_too_long", "Report reason must be 80 characters or fewer.");
        }

        var now = DateTimeOffset.UtcNow;
        var report = new AiAnswerReport
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Question = question,
            Answer = answer,
            Reason = reason,
            Details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
            ContextJson = JsonSerializer.Serialize(request.Context ?? new { }, JsonOptions),
            SourcesJson = JsonSerializer.Serialize(request.Sources ?? Array.Empty<AiChatSourceDto>(), JsonOptions),
            Status = "open",
            CreatedAt = now,
        };

        _db.AiAnswerReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);

        return new AiAnswerReportResponse(report.Id, report.Status, report.CreatedAt);
    }

    private async Task<User> ResolveActiveUserAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        if (supabaseUserId == Guid.Empty)
        {
            throw new AiStudyFeatureException(401, "missing_user_id", "Authenticated Supabase user id is missing or invalid.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new AiStudyFeatureException(404, "user_not_found", "Authenticated user has no profile in public.users.");

        if (!user.IsActive)
        {
            throw new AiStudyFeatureException(403, "user_inactive", "User account is inactive.");
        }

        return user;
    }

    private static string NormalizeRequired(string? value, string code, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new AiStudyFeatureException(400, code, message);
        }

        return normalized;
    }
}
