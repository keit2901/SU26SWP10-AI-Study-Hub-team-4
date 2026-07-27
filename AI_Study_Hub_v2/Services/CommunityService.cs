using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AI_Study_Hub_v2.Services;

public sealed class CommunityService : ICommunityService
{
    private const string PendingStatus = "Pending";
    private const string ResolvedStatus = "Resolved";
    private const string DismissedStatus = "Dismissed";
    private const string PendingReportConstraint = "ux_community_reports_pending_folder_reporter";
    private const int MaxTextLength = 2_000;

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
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            throw new CommunityException(400, "invalid_reason",
                "Reason cannot be empty.");
        }
        if (normalizedReason.Length > MaxTextLength)
        {
            throw new CommunityException(400, "reason_too_long",
                $"Reason must be {MaxTextLength:N0} characters or fewer.");
        }

        var profile = await ResolveProfileAsync(reportedByUserId, ct);

        var folder = await _db.Folders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId, ct)
            ?? throw new CommunityException(404, "folder_not_found", "Folder not found.");

        if (folder.ShareStatus != FolderStatus.Approved)
        {
            throw new CommunityException(400, "folder_not_shared",
                "Only approved folders can be reported.");
        }

        if (folder.UserId == profile.Id)
        {
            throw new CommunityException(400, "cannot_report_own_folder",
                "You cannot report your own folder.");
        }

        var alreadyReported = await _db.Set<CommunityReport>()
            .AnyAsync(r => r.FolderId == folderId
                && r.ReportedByUserId == profile.Id
                && r.Status == PendingStatus, ct);

        if (alreadyReported)
        {
            throw new CommunityException(409, "duplicate_report",
                "You have already submitted a pending report for this folder.");
        }

        var report = new CommunityReport
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            ReportedByUserId = profile.Id,
            Reason = normalizedReason,
            Status = PendingStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Set<CommunityReport>().Add(report);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: PendingReportConstraint,
            })
        {
            throw new CommunityException(409, "duplicate_report",
                "You have already submitted a pending report for this folder.");
        }

        _logger.LogInformation("Community report created: id={Id} folder={FolderId} user={UserId}",
            report.Id, folderId, profile.Id);

        return report.Id;
    }

    public async Task<IReadOnlyList<CommunityReportDto>> GetPendingReportsAsync(
        Guid reviewerSupabaseUserId,
        CancellationToken ct = default)
    {
        await ResolveReviewerProfileAsync(reviewerSupabaseUserId, ct);

        var reports = await _db.Set<CommunityReport>()
            .AsNoTracking()
            .Where(r => r.Status == PendingStatus)
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
        var profile = await ResolveReviewerProfileAsync(resolvedByUserId, ct);

        var report = await _db.Set<CommunityReport>()
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new CommunityException(404, "report_not_found", "Report not found.");

        if (!report.Status.Equals(PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommunityException(400, "report_already_resolved",
                "This report has already been resolved or dismissed.");
        }

        var normalizedStatus = status?.Trim();
        if (normalizedStatus?.Equals(ResolvedStatus, StringComparison.OrdinalIgnoreCase) == true)
        {
            normalizedStatus = ResolvedStatus;
        }
        else if (normalizedStatus?.Equals(DismissedStatus, StringComparison.OrdinalIgnoreCase) == true)
        {
            normalizedStatus = DismissedStatus;
        }
        else
        {
            throw new CommunityException(400, "invalid_status",
                "Status must be 'Resolved' or 'Dismissed'.");
        }

        var normalizedResolution = resolution?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedResolution))
        {
            throw new CommunityException(400, "resolution_required",
                "A resolution note is required.");
        }
        if (normalizedResolution.Length > MaxTextLength)
        {
            throw new CommunityException(400, "resolution_too_long",
                $"Resolution must be {MaxTextLength:N0} characters or fewer.");
        }

        report.Status = normalizedStatus;
        report.Resolution = normalizedResolution;
        report.ResolvedByUserId = profile.Id;
        report.ResolvedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Community report resolved: id={Id} status={Status} by={UserId}",
            report.Id, normalizedStatus, profile.Id);
    }

    private async Task<User> ResolveProfileAsync(Guid supabaseUserId, CancellationToken ct)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .Include(user => user.Role)
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

    private async Task<User> ResolveReviewerProfileAsync(Guid supabaseUserId, CancellationToken ct)
    {
        var profile = await ResolveProfileAsync(supabaseUserId, ct);
        var roleName = profile.Role.RoleName;
        if (!roleName.Equals(Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
            && !roleName.Equals(Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CommunityException(403, "reviewer_required",
                "Administrator or moderator access is required to review reports.");
        }

        return profile;
    }
}
