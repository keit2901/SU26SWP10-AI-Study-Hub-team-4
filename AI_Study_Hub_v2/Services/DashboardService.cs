using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Components.Pages.Dashboard;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public class DashboardService : IDashboardService
{
    private const string BucketName = "documents";
    private const int SignedUrlTtlSeconds = 300;

    private readonly AppDbContext _context;
    private readonly ISupabaseStorageClient _storage;

    public DashboardService(AppDbContext context, ISupabaseStorageClient storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task<AdminDashboardStatsDto> GetAdminStatsAsync(CancellationToken ct = default)
    {
        var totalUsers = await _context.Users.AsNoTracking().CountAsync(ct);
        var totalDocs = await _context.Documents.AsNoTracking().CountAsync(ct);
        
        // Mocked or estimated storage. For a real app, query Document file size sum or Storage bucket.
        // Assuming average 1MB per document for this demo.
        long totalStorageMb = totalDocs * 1; 

        // Count pending/processing as active sessions or jobs
        var activeJobs = await _context.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing)
            .CountAsync(ct);

        var failedJobs = await _context.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Failed)
            .CountAsync(ct);

        var indexedCount = await _context.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Ready)
            .CountAsync(ct);

        var processingCount = await _context.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing)
            .CountAsync(ct);

        var pendingCount = await _context.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Uploading)
            .CountAsync(ct);

        return new AdminDashboardStatsDto(
            TotalUsers: totalUsers,
            TotalDocuments: totalDocs,
            TotalStorageUsedMb: totalStorageMb,
            TotalActiveSessions: activeJobs,
            TotalFailedJobs: failedJobs,
            IndexedCount: indexedCount,
            ProcessingCount: processingCount,
            PendingCount: pendingCount
        );
    }

    public async Task<UserDashboardStatsDto> GetUserStatsAsync(Guid userId, CancellationToken ct = default)
    {
        var totalFolders = await _context.Folders.AsNoTracking().Where(f => f.UserId == userId).CountAsync(ct);
        
        var documents = await _context.Documents.AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => new { d.Id, d.Status, d.FileSizeBytes })
            .ToListAsync(ct);

        var totalDocs = documents.Count;
        
        // Sum the file sizes (assuming FileSizeBytes is in bytes)
        long totalBytes = documents.Sum(d => d.FileSizeBytes);
        long storageMb = totalBytes > 0 ? (totalBytes / (1024 * 1024)) : 0;
        if(storageMb == 0 && totalDocs > 0) storageMb = 1; // display at least 1MB if not 0

        var approved = documents.Count(d => d.Status == DocumentStatus.Ready);
        var pending = documents.Count(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing);
        var rejected = documents.Count(d => d.Status == DocumentStatus.Failed);

        // Fetch recent folders
        var recentDbFolders = await _context.Folders
            .AsNoTracking()
            .Include(f => f.Documents)
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(10)
            .ToListAsync(ct);

        var recentViewModels = recentDbFolders.Select(f => 
        {
            var firstDoc = f.Documents.FirstOrDefault();
            var subject = firstDoc?.SubjectCode ?? "N/A";
            var semester = firstDoc?.Semester ?? "N/A";

            string status = "Pending";
            if (f.Documents.Any(d => d.Status == DocumentStatus.Failed))
                status = "Rejected";
            else if (f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing))
                status = "Pending";
            else if (f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready))
                status = "Approved";

            return new FolderViewModel
            {
                Id = f.Id,
                Name = f.Name,
                SubjectCode = subject,
                Semester = semester,
                Status = status
            };
        }).ToList();

        return new UserDashboardStatsDto(
            TotalFolders: totalFolders,
            TotalDocuments: totalDocs,
            StorageUsedMb: storageMb,
            ApprovedDocuments: approved,
            PendingDocuments: pending,
            RejectedDocuments: rejected,
            RecentFolders: recentViewModels
        );
    }

    public async Task<System.Collections.Generic.List<DashboardSubjectDto>> GetSubjectsStatsAsync(CancellationToken ct = default)
    {
        var groups = await _context.Documents.AsNoTracking()
            .GroupBy(d => d.SubjectCode)
            .Select(g => new
            {
                SubjectCode = g.Key,
                DocumentCount = g.Count(),
                StorageUsedBytes = g.Sum(d => d.FileSizeBytes),
                LatestUploadDate = g.Max(d => (System.DateTimeOffset?)d.CreatedAt)
            })
            .ToListAsync(ct);

        return groups.Select(g => new DashboardSubjectDto(
            SubjectCode: string.IsNullOrWhiteSpace(g.SubjectCode) ? "N/A" : g.SubjectCode,
            DocumentCount: g.DocumentCount,
            StorageUsedMb: System.Math.Round((double)g.StorageUsedBytes / (1024 * 1024), 2),
            LatestUploadDate: g.LatestUploadDate
        )).OrderBy(s => s.SubjectCode).ToList();
    }

    public async Task<System.Collections.Generic.List<DashboardSemesterDto>> GetSemestersStatsAsync(CancellationToken ct = default)
    {
        var groups = await _context.Documents.AsNoTracking()
            .GroupBy(d => d.Semester)
            .Select(g => new
            {
                Semester = g.Key,
                DocumentCount = g.Count(),
                StorageUsedBytes = g.Sum(d => d.FileSizeBytes),
                LatestUploadDate = g.Max(d => (System.DateTimeOffset?)d.CreatedAt)
            })
            .ToListAsync(ct);

        return groups.Select(g => new DashboardSemesterDto(
            Semester: string.IsNullOrWhiteSpace(g.Semester) ? "N/A" : g.Semester,
            DocumentCount: g.DocumentCount,
            StorageUsedMb: System.Math.Round((double)g.StorageUsedBytes / (1024 * 1024), 2),
            LatestUploadDate: g.LatestUploadDate
        )).OrderBy(s => s.Semester).ToList();
    }

    public async Task<System.Collections.Generic.List<DocumentDto>> GetPendingDocumentsAsync(System.Guid? folderId = null, CancellationToken ct = default)
    {
        IQueryable<Document> query;

        if (folderId.HasValue)
        {
            // Folder-specific: all documents in the folder (any status)
            query = _context.Documents.AsNoTracking()
                .Where(d => d.FolderId == folderId.Value);
        }
        else
        {
            // Global view: all documents from ANY folder with PendingShare status
            query = _context.Documents.AsNoTracking()
                .Where(d => d.Folder != null && d.Folder.ShareStatus == FolderStatus.PendingShare);
        }

        var docs = await query
            .Include(d => d.Folder)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return docs.Select(d => new DocumentDto
        {
            Id = d.Id,
            FolderId = d.FolderId,
            FileName = d.FileName,
            FileSizeBytes = d.FileSizeBytes,
            MimeType = d.MimeType,
            SubjectCode = d.SubjectCode,
            Semester = d.Semester,
            PageCount = d.PageCount,
            Status = d.Status,
            ReviewStatus = d.ReviewStatus,
            ErrorMessage = d.ErrorMessage,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt,
            FolderName = d.Folder?.Name
        }).ToList();
    }

    public async Task<bool> ApproveDocumentAsync(System.Guid documentId, CancellationToken ct = default)
    {
        var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return false;

        doc.ReviewStatus = DocumentReviewStatus.Approved;
        doc.UpdatedAt = System.DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectDocumentAsync(System.Guid documentId, CancellationToken ct = default)
    {
        var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return false;

        doc.ReviewStatus = DocumentReviewStatus.Rejected;
        doc.ErrorMessage = "Rejected by administrator.";
        doc.UpdatedAt = System.DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UserAnalyticsDto> GetUserAnalyticsAsync(System.Guid userId, System.Guid? folderId = null, CancellationToken ct = default)
    {
        var query = _context.Documents.AsNoTracking().Where(d => d.UserId == userId);
        if (folderId.HasValue)
        {
            query = query.Where(d => d.FolderId == folderId.Value);
        }

        var totalDocuments = await query.CountAsync(ct);
        var approvedDocuments = await query.Where(d => d.Status == DocumentStatus.Ready).CountAsync(ct);
        double completionRate = totalDocuments > 0 ? System.Math.Round((double)approvedDocuments * 100 / totalDocuments, 1) : 0;
        
        var totalBytes = await query.SumAsync(d => d.FileSizeBytes, ct);
        double storageUsedMb = System.Math.Round((double)totalBytes / (1024 * 1024), 2);

        // Daily upload counts for last 7 days
        var today = new System.DateTimeOffset(System.DateTimeOffset.UtcNow.Date, System.TimeSpan.Zero);
        var last7Days = System.Linq.Enumerable.Range(0, 7)
            .Select(i => today.AddDays(-i))
            .Reverse()
            .ToList();

        var dailyCounts = new System.Collections.Generic.List<double>();
        var dailyLabels = new System.Collections.Generic.List<string>();
        var dailyApproved = new System.Collections.Generic.List<double>();
        var dailyRejected = new System.Collections.Generic.List<double>();

        foreach (var date in last7Days)
        {
            var nextDate = date.AddDays(1);
            var count = await query
                .Where(d => d.CreatedAt >= date && d.CreatedAt < nextDate)
                .CountAsync(ct);
            dailyCounts.Add(count);
            var approved = await query
                .Where(d => d.Status == DocumentStatus.Ready && d.CreatedAt >= date && d.CreatedAt < nextDate)
                .CountAsync(ct);
            dailyApproved.Add(approved);
            var rejected = await query
                .Where(d => d.Status == DocumentStatus.Failed && d.CreatedAt >= date && d.CreatedAt < nextDate)
                .CountAsync(ct);
            dailyRejected.Add(rejected);
            dailyLabels.Add(date.ToString("ddd"));
        }

        // Common issues (Failed documents error messages)
        var issues = await query
            .Where(d => d.Status == DocumentStatus.Failed && d.ErrorMessage != null)
            .GroupBy(d => d.ErrorMessage)
            .Select(g => new AnalyticsIssueDto(g.Key ?? "Unknown Error", g.Count()))
            .ToListAsync(ct);

        // Recent documents
        var docs = await query
            .Include(d => d.Chunks)
            .OrderByDescending(d => d.CreatedAt)
            .Take(10)
            .Select(d => new AnalyticsDocumentDto(
                d.Id,
                d.FileName,
                d.Status.ToString(),
                d.CreatedAt,
                d.PageCount,
                d.Chunks.Count,
                null,   // FolderName
                null    // FolderSharedAt
            ))
            .ToListAsync(ct);

        return new UserAnalyticsDto(
            TotalDocuments: totalDocuments,
            CompletionRate: completionRate,
            AvgProcessingTimeHrs: 1.2, // Stable estimation
            StorageUsedMb: storageUsedMb,
            DailyUploadCounts: dailyCounts,
            DailyUploadLabels: dailyLabels,
            DailyApprovedCounts: dailyApproved,
            DailyRejectedCounts: dailyRejected,
            CommonIssues: issues,
            RecentDocuments: docs
        );
    }

    public async Task<string?> GetDocumentSignedUrlAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (doc == null) return null;

        try
        {
            return await _storage.CreateSignedUrlAsync(
                BucketName, doc.StoragePath, SignedUrlTtlSeconds, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<UserAnalyticsDto> GetAdminAnalyticsAsync(Guid? folderId = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        IQueryable<Document> query;

        if (folderId.HasValue)
        {
            // Specific folder: all its documents (any status)
            query = _context.Documents.AsNoTracking()
                .Include(d => d.Chunks)
                .Include(d => d.Folder)
                .Where(d => d.FolderId == folderId.Value);
        }
        else
        {
            // Global: ALL documents across all folders (and orphans)
            query = _context.Documents.AsNoTracking()
                .Include(d => d.Chunks)
                .Include(d => d.Folder);
        }

        var totalDocuments = await query.CountAsync(ct);
        var approvedDocuments = await query.Where(d => d.ReviewStatus == DocumentReviewStatus.Approved).CountAsync(ct);
        double completionRate = totalDocuments > 0 ? System.Math.Round((double)approvedDocuments * 100 / totalDocuments, 1) : 0;

        var totalBytes = await query.SumAsync(d => d.FileSizeBytes, ct);
        double storageUsedMb = System.Math.Round((double)totalBytes / (1024 * 1024), 2);

        // Daily counts for last 7 days (based on UpdatedAt of moderator action)
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var last7Days = Enumerable.Range(0, 7)
            .Select(i => today.AddDays(-i))
            .Reverse()
            .ToList();

        var dailyApproved = new List<double>();
        var dailyRejected = new List<double>();
        var dailyLabels = new List<string>();

        foreach (var date in last7Days)
        {
            var nextDate = date.AddDays(1);
            var approved = await query
                .Where(d => d.ReviewStatus == DocumentReviewStatus.Approved && d.UpdatedAt >= date && d.UpdatedAt < nextDate)
                .CountAsync(ct);
            dailyApproved.Add(approved);

            var rejected = await query
                .Where(d => d.ReviewStatus == DocumentReviewStatus.Rejected && d.UpdatedAt >= date && d.UpdatedAt < nextDate)
                .CountAsync(ct);
            dailyRejected.Add(rejected);

            dailyLabels.Add(date.ToString("ddd"));
        }

        // Common issues (only for moderator-rejected documents)
        var issues = await query
            .Where(d => d.ReviewStatus == DocumentReviewStatus.Rejected && d.ErrorMessage != null)
            .GroupBy(d => d.ErrorMessage)
            .Select(g => new AnalyticsIssueDto(g.Key ?? "Unknown Error", g.Count()))
            .ToListAsync(ct);

        // Total document count for pagination (before skip/take)
        var totalDocCount = await query.CountAsync(ct);

        // All documents with pagination, sorted by folder shared date (most recent first)
        IQueryable<Document> orderedQuery;
        if (folderId.HasValue)
        {
            // Folder-specific: sort by updated date desc
            orderedQuery = query.OrderByDescending(d => d.UpdatedAt);
        }
        else
        {
            // Global: documents from shared folders first, sorted by SharedAt desc,
            // then unshared/loose documents, then by created date desc
            orderedQuery = query
                .OrderBy(d => d.Folder.SharedAt == null ? 1 : 0)
                .ThenByDescending(d => d.Folder.SharedAt)
                .ThenByDescending(d => d.CreatedAt);
        }

        var docs = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new AnalyticsDocumentDto(
                d.Id,
                d.FileName,
                d.ReviewStatus.ToString(),
                d.CreatedAt,
                d.PageCount,
                d.Chunks.Count,
                d.Folder != null ? d.Folder.Name : null,
                d.Folder != null ? d.Folder.SharedAt : null
            ))
            .ToListAsync(ct);

        return new UserAnalyticsDto(
            TotalDocuments: totalDocuments,
            CompletionRate: completionRate,
            AvgProcessingTimeHrs: 1.2,
            StorageUsedMb: storageUsedMb,
            DailyUploadCounts: dailyApproved,          // reuse field — shows approved trend
            DailyUploadLabels: dailyLabels,
            DailyApprovedCounts: dailyApproved,
            DailyRejectedCounts: dailyRejected,
            CommonIssues: issues,
            RecentDocuments: docs,
            TotalDocumentCount: totalDocCount,
            Page: page,
            PageSize: pageSize
        );
    }

    public async Task<ActivityTrendsDto> GetActivityTrendsAsync(string period = "day", CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        int buckets;
        Func<int, DateTime> bucketStart;
        Func<DateTime, string> labelFormatter;

        switch (period?.ToLowerInvariant())
        {
            case "week":
                buckets = 8;
                bucketStart = i => now.AddDays(-(i + 1) * 7).Date;
                labelFormatter = dt => $"W{ISOWeek(dt)}";
                break;
            case "month":
                buckets = 6;
                bucketStart = i => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
                labelFormatter = dt => dt.ToString("MMM");
                break;
            default:
                period = "day";
                buckets = 7;
                bucketStart = i => now.AddDays(-i).Date;
                labelFormatter = dt => dt.ToString("ddd");
                break;
        }

        var points = new List<ActivityTrendPoint>();
        for (var i = buckets - 1; i >= 0; i--)
        {
            var start = bucketStart(i);
            var end = i == 0
                ? now.AddDays(1).Date
                : bucketStart(i - 1);

            var allDocs = await _context.Documents
                .AsNoTracking()
                .Where(d => d.CreatedAt >= start && d.CreatedAt < end)
                .CountAsync(ct);

            var failed = await _context.Documents
                .AsNoTracking()
                .Where(d => d.CreatedAt >= start && d.CreatedAt < end && d.Status == DocumentStatus.Failed)
                .CountAsync(ct);

            points.Add(new ActivityTrendPoint(
                Label: labelFormatter(start),
                Uploads: allDocs,
                Documents: allDocs,
                Failed: failed
            ));
        }

        return new ActivityTrendsDto(Period: period, Points: points);
    }

    private static int ISOWeek(DateTime dt)
    {
        var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
        return cal.GetWeekOfYear(dt, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }
}
