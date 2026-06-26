using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class CommunityService : ICommunityService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CommunityService> _logger;

    public CommunityService(AppDbContext db, ILogger<CommunityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Guid> ReportFolderAsync(
        Guid reportedByUserId,
        Guid folderId,
        string reason,
        CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(reportedByUserId, ct);

        var folder = await _db.Folders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId, ct)
            ?? throw new CommunityException(404, "folder_not_found", "Folder not found.");

        if (!folder.IsShared)
        {
            throw new CommunityException(400, "folder_not_shared",
                "Only shared folders can be reported.");
        }

        if (folder.UserId == profile.Id)
        {
            throw new CommunityException(400, "cannot_report_own_folder",
                "You cannot report your own folder.");
        }

        var alreadyReported = await _db.Set<CommunityReport>()
            .AnyAsync(r => r.FolderId == folderId
                && r.ReportedByUserId == profile.Id
                && r.Status == "Pending", ct);

        if (alreadyReported)
        {
            throw new CommunityException(409, "duplicate_report",
                "You have already submitted a pending report for this folder.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CommunityException(400, "invalid_reason",
                "Reason cannot be empty.");
        }

        var report = new CommunityReport
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            ReportedByUserId = profile.Id,
            Reason = reason.Trim(),
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Set<CommunityReport>().Add(report);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Community report created: id={Id} folder={FolderId} user={UserId}",
            report.Id, folderId, profile.Id);

        return report.Id;
    }

    public async Task<IReadOnlyList<CommunityReportDto>> GetPendingReportsAsync(
        CancellationToken ct = default)
    {
        var reports = await _db.Set<CommunityReport>()
            .AsNoTracking()
            .Where(r => r.Status == "Pending")
            .Include(r => r.Folder)
            .Include(r => r.ReportedBy)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new CommunityReportDto(
                r.Id,
                r.FolderId,
                r.Folder.Name,
                r.ReportedByUserId,
                r.ReportedBy.FullName ?? r.ReportedBy.Username,
                r.Reason,
                r.Status,
                r.Resolution,
                r.CreatedAt,
                r.ResolvedAt))
            .ToListAsync(ct);

        return reports;
    }

    public async Task ResolveReportAsync(
        Guid resolvedByUserId,
        Guid reportId,
        string status,
        string? resolution,
        CancellationToken ct = default)
    {
        var profile = await ResolveProfileAsync(resolvedByUserId, ct);

        var report = await _db.Set<CommunityReport>()
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new CommunityException(404, "report_not_found", "Report not found.");

        if (report.Status != "Pending")
        {
            throw new CommunityException(400, "report_already_resolved",
                "This report has already been resolved or dismissed.");
        }

        if (status != "Resolved" && status != "Dismissed")
        {
            throw new CommunityException(400, "invalid_status",
                "Status must be 'Resolved' or 'Dismissed'.");
        }

        report.Status = status;
        report.Resolution = resolution?.Trim();
        report.ResolvedByUserId = profile.Id;
        report.ResolvedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Community report resolved: id={Id} status={Status} by={UserId}",
            report.Id, status, profile.Id);
    }

    private async Task<User> ResolveProfileAsync(Guid supabaseUserId, CancellationToken ct)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new CommunityException(404, "user_not_found",
                "Authenticated user has no profile in public.users.");

        if (!profile.IsActive)
        {
            throw new CommunityException(403, "user_inactive",
                "User account is inactive.");
        }

        return profile;
    }
}
