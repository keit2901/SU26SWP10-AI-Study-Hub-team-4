using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Components.Pages.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
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

        return new AdminDashboardStatsDto(
            TotalUsers: totalUsers,
            TotalDocuments: totalDocs,
            TotalStorageUsedMb: totalStorageMb,
            TotalActiveSessions: activeJobs,
            TotalFailedJobs: failedJobs
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

    public async Task<System.Collections.Generic.List<DocumentDto>> GetPendingDocumentsAsync(CancellationToken ct = default)
    {
        var docs = await _context.Documents.AsNoTracking()
            .Where(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing)
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
            ErrorMessage = d.ErrorMessage,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        }).ToList();
    }

    public async Task<bool> ApproveDocumentAsync(System.Guid documentId, CancellationToken ct = default)
    {
        var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return false;

        doc.Status = DocumentStatus.Ready;
        doc.UpdatedAt = System.DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectDocumentAsync(System.Guid documentId, CancellationToken ct = default)
    {
        var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return false;

        doc.Status = DocumentStatus.Failed;
        doc.ErrorMessage = "Rejected by administrator.";
        doc.UpdatedAt = System.DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UserAnalyticsDto> GetUserAnalyticsAsync(System.Guid userId, CancellationToken ct = default)
    {
        var totalDocuments = await _context.Documents.AsNoTracking().Where(d => d.UserId == userId).CountAsync(ct);
        var approvedDocuments = await _context.Documents.AsNoTracking().Where(d => d.UserId == userId && d.Status == DocumentStatus.Ready).CountAsync(ct);
        double completionRate = totalDocuments > 0 ? System.Math.Round((double)approvedDocuments * 100 / totalDocuments, 1) : 0;
        
        var totalBytes = await _context.Documents.AsNoTracking().Where(d => d.UserId == userId).SumAsync(d => d.FileSizeBytes, ct);
        double storageUsedMb = System.Math.Round((double)totalBytes / (1024 * 1024), 2);

        // Daily upload counts for last 7 days
        var today = new System.DateTimeOffset(System.DateTimeOffset.UtcNow.Date, System.TimeSpan.Zero);
        var last7Days = System.Linq.Enumerable.Range(0, 7)
            .Select(i => today.AddDays(-i))
            .Reverse()
            .ToList();

        var dailyCounts = new System.Collections.Generic.List<double>();
        var dailyLabels = new System.Collections.Generic.List<string>();

        foreach (var date in last7Days)
        {
            var nextDate = date.AddDays(1);
            var count = await _context.Documents.AsNoTracking()
                .Where(d => d.UserId == userId && d.CreatedAt >= date && d.CreatedAt < nextDate)
                .CountAsync(ct);
            dailyCounts.Add(count);
            dailyLabels.Add(date.ToString("ddd"));
        }

        // Common issues (Failed documents error messages)
        var issues = await _context.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && d.Status == DocumentStatus.Failed && d.ErrorMessage != null)
            .GroupBy(d => d.ErrorMessage)
            .Select(g => new AnalyticsIssueDto(g.Key ?? "Unknown Error", g.Count()))
            .ToListAsync(ct);

        // Recent documents
        var docs = await _context.Documents.AsNoTracking()
            .Include(d => d.Chunks)
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(10)
            .Select(d => new AnalyticsDocumentDto(
                d.Id,
                d.FileName,
                d.Status.ToString(),
                d.CreatedAt,
                d.PageCount,
                d.Chunks.Count
            ))
            .ToListAsync(ct);

        return new UserAnalyticsDto(
            TotalDocuments: totalDocuments,
            CompletionRate: completionRate,
            AvgProcessingTimeHrs: 1.2, // Stable estimation
            StorageUsedMb: storageUsedMb,
            DailyUploadCounts: dailyCounts,
            DailyUploadLabels: dailyLabels,
            CommonIssues: issues,
            RecentDocuments: docs
        );
    }
}
