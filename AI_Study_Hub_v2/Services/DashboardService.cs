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
    private readonly IAuditLogService _audit;
    private readonly IFolderShareAiModerator _shareAiModerator;

    public DashboardService(
        AppDbContext context,
        ISupabaseStorageClient storage,
        IAuditLogService audit,
        IFolderShareAiModerator shareAiModerator)
    {
        _context = context;
        _storage = storage;
        _audit = audit;
        _shareAiModerator = shareAiModerator;
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dailyTokensUsed = await _context.Users.AsNoTracking()
            .Where(u => u.TokenUsageDate == today)
            .SumAsync(u => (long)u.TokensUsedToday, ct);

        var dailyTokenQuota = await _context.Users.AsNoTracking()
            .SumAsync(u => (long)u.DailyTokenQuota, ct);

        return new AdminDashboardStatsDto(
            TotalUsers: totalUsers,
            TotalDocuments: totalDocs,
            TotalStorageUsedMb: totalStorageMb,
            TotalActiveSessions: activeJobs,
            TotalFailedJobs: failedJobs,
            IndexedCount: indexedCount,
            ProcessingCount: processingCount,
            PendingCount: pendingCount,
            DailyTokensUsed: dailyTokensUsed,
            DailyTokenQuota: dailyTokenQuota
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

            string status = "Private";
            if (f.ShareStatus != FolderStatus.None)
            {
                status = f.ShareStatus switch
                {
                    FolderStatus.PendingShare => "Pending Share",
                    FolderStatus.Approved => "Shared",
                    FolderStatus.Rejected => "Rejected",
                    _ => "Private"
                };
            }
            else if (f.Documents.Any(d => d.Status == DocumentStatus.Failed))
            {
                status = "Failed";
            }
            else if (f.Documents.Any(d => d.Status == DocumentStatus.Uploading || d.Status == DocumentStatus.Processing))
            {
                status = "Processing";
            }
            else if (f.Documents.Any() && f.Documents.All(d => d.Status == DocumentStatus.Ready))
            {
                status = "Private";
            }
            else
            {
                status = "Empty";
            }

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
        var folderSchema = await GetFolderSchemaCapabilitiesAsync(ct);
        IQueryable<Document> query;

        if (folderId.HasValue)
        {
            query = _context.Documents.AsNoTracking()
                .Where(d => d.FolderId == folderId.Value);
        }
        else
        {
            query = _context.Documents.AsNoTracking()
                .Where(d => d.Folder != null && d.Folder.ShareStatus == FolderStatus.PendingShare);
        }

        if (folderSchema.HasFullModernShareFlowColumns)
        {
            return await query
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new DocumentDto
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
                    FolderName = d.Folder != null ? d.Folder.Name : null,
                    FolderShareStatus = d.Folder != null ? d.Folder.ShareStatus : FolderStatus.None,
                    ShareReviewSource = d.Folder != null ? d.Folder.ShareReviewSource : null,
                    ShareFailureCount = d.Folder != null && folderSchema.HasShareFeedbackColumns ? d.Folder.ShareFailureCount : 0,
                    StudentFeedbackReason = d.Folder != null && folderSchema.HasStudentFeedbackWorkflowColumns ? d.Folder.StudentFeedbackReason : null,
                    AppealMessage = d.Folder != null && folderSchema.HasStudentFeedbackWorkflowColumns ? d.Folder.AppealMessage : null
                })
                .ToListAsync(ct);
        }

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto
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
                FolderName = d.Folder != null ? d.Folder.Name : null,
                FolderShareStatus = d.Folder != null ? d.Folder.ShareStatus : FolderStatus.None,
                ShareReviewSource = null,
                ShareFailureCount = d.Folder != null && folderSchema.HasShareFeedbackColumns ? d.Folder.ShareFailureCount : 0,
                StudentFeedbackReason = d.Folder != null && folderSchema.HasStudentFeedbackWorkflowColumns ? d.Folder.StudentFeedbackReason : null,
                AppealMessage = d.Folder != null && folderSchema.HasStudentFeedbackWorkflowColumns ? d.Folder.AppealMessage : null
            })
            .ToListAsync(ct);
    }

    public async Task<DocumentAiReviewResultDto?> AiReviewDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var folderSchema = await GetFolderSchemaCapabilitiesAsync(ct);
        var docQuery = _context.Documents
            .Include(d => d.Chunks)
            .AsQueryable();

        if (folderSchema.HasFullModernShareFlowColumns)
        {
            docQuery = docQuery.Include(d => d.Folder);
        }

        var doc = await docQuery.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null)
        {
            return null;
        }

        Folder reviewFolder;
        if (doc.Folder is not null)
        {
            reviewFolder = doc.Folder;
        }
        else if (doc.FolderId.HasValue)
        {
            var folderInfo = await _context.Folders
                .AsNoTracking()
                .Where(f => f.Id == doc.FolderId.Value)
                .Select(f => new { f.Id, f.Name, f.Description, f.ShareStatus })
                .FirstOrDefaultAsync(ct);

            reviewFolder = folderInfo is null
                ? new Folder
                {
                    Id = doc.FolderId.Value,
                    Name = "Single Document Review",
                    Description = $"AI moderation review for {doc.FileName}."
                }
                : new Folder
                {
                    Id = folderInfo.Id,
                    Name = folderInfo.Name,
                    Description = folderInfo.Description,
                    ShareStatus = folderInfo.ShareStatus
                };
        }
        else
        {
            reviewFolder = new Folder
            {
                Id = Guid.Empty,
                Name = "Single Document Review",
                Description = $"AI moderation review for {doc.FileName}."
            };
        }

        var extractedTexts = doc.Chunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => chunk.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToList();

        var decision = _shareAiModerator.Evaluate(reviewFolder, [doc], extractedTexts);
        var isApproved = decision.Outcome == FolderShareModerationOutcome.AutoApproved;

        doc.ReviewStatus = isApproved
            ? DocumentReviewStatus.Approved
            : DocumentReviewStatus.Rejected;
        doc.ErrorMessage = isApproved ? null : decision.Reason;
        doc.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        await UpdateFolderShareStatusFromDocumentModerationAsync(
            doc.FolderId,
            reviewSource: "AI_ASSIST",
            moderationReason: decision.Reason,
            confidence: decision.Confidence,
            ct);

        await _context.SaveChangesAsync(ct);

        _audit.Add(
            null,
            isApproved ? "DOCUMENT_AI_APPROVED" : "DOCUMENT_AI_REJECTED",
            "Document",
            documentId.ToString(),
            isApproved ? "Low" : "Medium");

        return new DocumentAiReviewResultDto(
            doc.Id,
            doc.ReviewStatus,
            "AI",
            decision.Reason,
            decision.Confidence);
    }

    public async Task<bool> ApproveDocumentAsync(System.Guid documentId, CancellationToken ct = default)
    {
        var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return false;

        doc.ReviewStatus = DocumentReviewStatus.Approved;
        doc.ErrorMessage = null;
        doc.UpdatedAt = System.DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        await UpdateFolderShareStatusFromDocumentModerationAsync(
            doc.FolderId,
            reviewSource: "HUMAN",
            moderationReason: null,
            confidence: null,
            ct);

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RejectDocumentAsync(System.Guid documentId, string? reason = null, CancellationToken ct = default)
    {
        var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return false;

        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Rejected by moderator."
            : reason.Trim();

        doc.ReviewStatus = DocumentReviewStatus.Rejected;
        doc.ErrorMessage = normalizedReason;
        doc.UpdatedAt = System.DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        await UpdateFolderShareStatusFromDocumentModerationAsync(
            doc.FolderId,
            reviewSource: "HUMAN",
            moderationReason: normalizedReason,
            confidence: null,
            ct);

        await _context.SaveChangesAsync(ct);
        return true;
    }

    private async Task UpdateFolderShareStatusFromDocumentModerationAsync(
        Guid? folderId,
        string reviewSource,
        string? moderationReason,
        double? confidence,
        CancellationToken ct)
    {
        if (!folderId.HasValue)
        {
            return;
        }

        var schema = await GetFolderSchemaCapabilitiesAsync(ct);
        if (!schema.HasFullModernShareFlowColumns)
        {
            var folderInfo = await _context.Folders
                .AsNoTracking()
                .Where(f => f.Id == folderId.Value)
                .Select(f => new
                {
                    f.Id,
                    f.ShareStatus,
                    ShareFailureCount = schema.HasShareFeedbackColumns ? f.ShareFailureCount : 0
                })
                .FirstOrDefaultAsync(ct);

            if (folderInfo == null || folderInfo.ShareStatus != FolderStatus.PendingShare)
            {
                return;
            }

            var statuses = await _context.Documents
                .AsNoTracking()
                .Where(d => d.FolderId == folderId.Value)
                .Select(d => d.ReviewStatus)
                .ToListAsync(ct);

            if (statuses.Count == 0)
            {
                return;
            }

            var hasRejectedDocumentCompatibility = statuses.Any(status => status == DocumentReviewStatus.Rejected);
            var allDocumentsApprovedCompatibility = statuses.All(status => status == DocumentReviewStatus.Approved);
            if (!hasRejectedDocumentCompatibility && !allDocumentsApprovedCompatibility)
            {
                return;
            }

            var nowCompatibility = DateTimeOffset.UtcNow;
            if (hasRejectedDocumentCompatibility)
            {
                if (schema.HasShareFeedbackColumns)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.Rejected},
    shared_at = NULL,
    share_failure_count = share_failure_count + 1,
    updated_at = {nowCompatibility}
WHERE id = {folderId.Value}", ct);
                }
                else
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.Rejected},
    shared_at = NULL,
    updated_at = {nowCompatibility}
WHERE id = {folderId.Value}", ct);
                }
            }
            else
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE folders
SET share_status = {(int)FolderStatus.Approved},
    shared_at = {nowCompatibility},
    updated_at = {nowCompatibility}
WHERE id = {folderId.Value}", ct);
            }

            return;
        }

        var folder = await _context.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.Id == folderId.Value, ct);
        if (folder == null || folder.ShareStatus != FolderStatus.PendingShare)
        {
            return;
        }

        var documents = folder.Documents.ToList();
        if (documents.Count == 0)
        {
            return;
        }

        var hasRejectedDocument = documents.Any(document => document.ReviewStatus == DocumentReviewStatus.Rejected);
        var allDocumentsApproved = documents.All(document => document.ReviewStatus == DocumentReviewStatus.Approved);
        if (!hasRejectedDocument && !allDocumentsApproved)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        folder.ShareReviewSource = reviewSource;
        folder.RequiresHumanReview = false;
        folder.AppealRequestedAt = null;
        folder.AppealMessage = null;
        folder.UpdatedAt = now;

        if (string.Equals(reviewSource, "AI_ASSIST", StringComparison.OrdinalIgnoreCase))
        {
            folder.AiReviewReason = moderationReason;
            folder.AiReviewConfidence = confidence;
            folder.HumanReviewReason = null;
        }
        else
        {
            folder.HumanReviewReason = moderationReason;
            folder.AiReviewReason = null;
            folder.AiReviewConfidence = null;
        }

        if (hasRejectedDocument)
        {
            folder.ShareStatus = FolderStatus.Rejected;
            folder.SharedAt = null;
            folder.ShareFailureCount += 1;
        }
        else if (allDocumentsApproved)
        {
            folder.ShareStatus = FolderStatus.Approved;
            folder.SharedAt = now;
            folder.AiReviewReason = null;
            folder.AiReviewConfidence = null;
            folder.HumanReviewReason = null;
            folder.AppealRequestedAt = null;
            folder.AppealMessage = null;
            folder.StudentFeedbackReason = null;
        }
    }

    private async Task<FolderSchemaCapabilities> GetFolderSchemaCapabilitiesAsync(CancellationToken ct)
    {
        var columnNames = await _context.Database
            .SqlQueryRaw<string>(@"
SELECT column_name
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = 'folders'")
            .ToListAsync(ct);

        var columns = columnNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasShareFeedbackColumns = columns.Contains("share_failure_count");

            var hasStudentFeedbackWorkflowColumns =
                columns.Contains("student_feedback_reason") &&
            columns.Contains("appeal_message");

        var hasFullModernShareFlowColumns =
            columns.Contains("share_review_source") &&
            columns.Contains("ai_review_reason") &&
            columns.Contains("ai_review_confidence") &&
            columns.Contains("human_review_reason") &&
            hasShareFeedbackColumns &&
            columns.Contains("student_feedback_reason") &&
            columns.Contains("requires_human_review") &&
            columns.Contains("appeal_requested_at") &&
            columns.Contains("appeal_message");

        return new FolderSchemaCapabilities(
            hasShareFeedbackColumns,
            hasStudentFeedbackWorkflowColumns,
            hasFullModernShareFlowColumns);
    }

    private sealed record FolderSchemaCapabilities(
        bool HasShareFeedbackColumns,
        bool HasStudentFeedbackWorkflowColumns,
        bool HasFullModernShareFlowColumns);

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

    public async Task<UserAnalyticsDto> GetAdminAnalyticsAsync(Guid? folderId = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
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
            case "30day":
                buckets = 30;
                bucketStart = i => now.AddDays(-i).Date;
                labelFormatter = dt => dt.ToString("M/d");
                break;
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
