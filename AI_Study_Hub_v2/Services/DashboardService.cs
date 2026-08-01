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
    private const int MaxModerationReasonLength = 2_000;

    private readonly AppDbContext _context;
    private readonly ISupabaseStorageClient _storage;
    private readonly IAuditLogService _audit;
    private readonly IFolderShareAiModerator _shareAiModerator;
    private readonly IUserNotificationService _notifications;
    private readonly IFolderPublicationStateService _publicationState;

    public DashboardService(
        AppDbContext context,
        ISupabaseStorageClient storage,
        IAuditLogService audit,
        IFolderShareAiModerator shareAiModerator,
        IUserNotificationService notifications,
        IFolderPublicationStateService publicationState)
    {
        _context = context;
        _storage = storage;
        _audit = audit;
        _shareAiModerator = shareAiModerator;
        _notifications = notifications;
        _publicationState = publicationState;
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

        var pendingEscalationCount = await _context.DocumentEscalations.AsNoTracking()
            .Where(e => e.EscalationStatus == "Pending")
            .CountAsync(ct);
        var pendingEscalatedDocumentCount = await _context.DocumentEscalationItems.AsNoTracking()
            .Where(item => item.ResolutionStatus == "Pending"
                && item.DocumentId != null
                && item.Escalation.EscalationStatus == "Pending")
            .Select(item => item.DocumentId)
            .Distinct()
            .CountAsync(ct);

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
            DailyTokenQuota: dailyTokenQuota,
            PendingEscalationCount: pendingEscalationCount,
            PendingEscalatedDocumentCount: pendingEscalatedDocumentCount
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

    public async Task<List<DocumentDto>> GetPendingModerationDocumentsAsync(Guid supabaseUserId, Guid? folderId, CancellationToken ct)
    {
        await EnsureModeratorAsync(supabaseUserId, ct);
        IQueryable<Document> query = _context.Documents.AsNoTracking()
            .Where(d => d.Folder != null
                && (d.Folder.ShareStatus == FolderStatus.PendingShare || d.Folder.ShareStatus == FolderStatus.Approved)
                && d.Status == DocumentStatus.Ready
                && d.ReviewStatus == DocumentReviewStatus.None);
        if (folderId.HasValue)
            query = query.Where(d => d.FolderId == folderId.Value);

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
                ModerationGeneration = d.ModerationGeneration,
                ErrorMessage = d.ErrorMessage,
                ModerationReason = d.ErrorMessage,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                FolderName = d.Folder != null ? d.Folder.Name : null,
                FolderShareStatus = d.Folder != null ? d.Folder.ShareStatus : FolderStatus.None,
                ShareReviewSource = d.Folder != null ? d.Folder.ShareReviewSource : null,
                ShareFailureCount = d.Folder != null ? d.Folder.ShareFailureCount : 0,
                StudentFeedbackReason = d.Folder != null ? d.Folder.StudentFeedbackReason : null,
                AppealMessage = d.Folder != null ? d.Folder.AppealMessage : null
            })
            .ToListAsync(ct);
    }

    public async Task<DocumentAiReviewResultDto?> AiReviewDocumentAsync(Guid supabaseUserId, Guid documentId, CancellationToken ct)
    {
        var caller = await EnsureModeratorAsync(supabaseUserId, ct);
        var doc = await GetActionableModerationDocumentAsync(documentId, ct);
        var reviewFolder = doc.Folder!;

        var extractedTexts = await _context.DocumentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => chunk.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToListAsync(ct);

        var decision = _shareAiModerator.Evaluate(reviewFolder, [doc], extractedTexts);
        var advisoryOutcome = decision.Outcome switch
        {
            FolderShareModerationOutcome.AutoApproved => DocumentAiAdvisoryOutcome.Approve,
            FolderShareModerationOutcome.NeedsHumanReview => DocumentAiAdvisoryOutcome.NeedsHumanReview,
            FolderShareModerationOutcome.AutoRejected => DocumentAiAdvisoryOutcome.Reject,
            _ => throw new InvalidOperationException($"Unsupported AI moderation outcome: {decision.Outcome}.")
        };

        _audit.Add(
            caller.User.Id,
            "DOCUMENT_AI_REVIEWED",
            "Document",
            documentId.ToString(),
            "Low");

        await _context.SaveChangesAsync(ct);

        return new DocumentAiReviewResultDto(
            doc.Id,
            doc.ReviewStatus,
            advisoryOutcome,
            "AI_ADVISORY",
            decision.Reason,
            decision.Confidence);
    }

    public async Task ApproveDocumentAsync(Guid supabaseUserId, Guid documentId, CancellationToken ct)
    {
        var reviewer = await EnsureModeratorAsync(supabaseUserId, ct);
        await ApplyDirectDecisionAsync(documentId, DocumentReviewStatus.Approved, null, reviewer.RoleLabel, ct);
    }

    public async Task RejectDocumentAsync(Guid supabaseUserId, Guid documentId, string? reason, CancellationToken ct)
    {
        var reviewer = await EnsureModeratorAsync(supabaseUserId, ct);
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Rejected by moderator."
            : reason.Trim();
        if (normalizedReason.Length > MaxModerationReasonLength)
        {
            throw DashboardModerationException.ReasonTooLong(MaxModerationReasonLength);
        }

        await ApplyDirectDecisionAsync(documentId, DocumentReviewStatus.Rejected, normalizedReason, reviewer.RoleLabel, ct);
    }

    private async Task ApplyDirectDecisionAsync(
        Guid documentId,
        DocumentReviewStatus decision,
        string? reason,
        string reviewerRoleLabel,
        CancellationToken ct)
    {
        // Resolve the document and folder separately so unknown documents remain 404 and
        // known-but-terminal documents remain 409. The conditional write below deliberately
        // uses scalar document columns only; navigation predicates are not translatable by EF.
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        var actionableDocument = await GetActionableModerationDocumentAsync(documentId, ct);
        var authoritativeFolder = actionableDocument.Folder!;
        var affected = _context.Database.IsRelational()
            ? await _context.Documents
                .Where(document => document.Id == documentId
                    && document.FolderId == authoritativeFolder.Id
                    && document.UserId == authoritativeFolder.UserId
                    && document.Status == DocumentStatus.Ready
                    && document.ReviewStatus == DocumentReviewStatus.None)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(document => document.ReviewStatus, decision)
                    .SetProperty(document => document.ErrorMessage, decision == DocumentReviewStatus.Rejected ? reason : null)
                    .SetProperty(document => document.UpdatedAt, now), ct)
            : ApplyInMemoryDirectDecision(documentId, authoritativeFolder, decision, reason, now);
        if (affected != 1)
            throw DashboardModerationException.NotActionable();

        if (_context.Database.IsRelational())
        {
            _context.ChangeTracker.Clear();
        }
        var document = await _context.Documents.Include(item => item.Folder).FirstAsync(item => item.Id == documentId, ct);
        var folder = document.Folder ?? throw DashboardModerationException.NotActionable();
        var folderDocuments = await _context.Documents.Where(item => item.FolderId == folder.Id).ToListAsync(ct);
        _publicationState.Recompute(folder, folderDocuments, now);
        _notifications.StageDocumentModerationFinal(document, folder, reviewerRoleLabel, reason, now);
        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private int ApplyInMemoryDirectDecision(
        Guid documentId,
        Folder authoritativeFolder,
        DocumentReviewStatus decision,
        string? reason,
        DateTimeOffset now)
    {
        var document = _context.Documents.Local.FirstOrDefault(item => item.Id == documentId);
        if (document is null || document.FolderId != authoritativeFolder.Id || document.UserId != authoritativeFolder.UserId
            || document.Status != DocumentStatus.Ready || document.ReviewStatus != DocumentReviewStatus.None)
            return 0;

        document.ReviewStatus = decision;
        document.ErrorMessage = decision == DocumentReviewStatus.Rejected ? reason : null;
        document.UpdatedAt = now;
        return 1;
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

    public async Task<string?> GetModerationDocumentSignedUrlAsync(Guid supabaseUserId, Guid documentId, CancellationToken ct)
    {
        var caller = await EnsureModeratorAsync(supabaseUserId, ct);
        var doc = await _context.Documents
            .Include(d => d.Folder)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (doc == null)
            throw DashboardModerationException.NotFound();
        var isActionable = doc.Folder?.ShareStatus is (FolderStatus.PendingShare or FolderStatus.Approved)
            && doc.Status == DocumentStatus.Ready
            && doc.ReviewStatus == DocumentReviewStatus.None;
        if (!isActionable && !await IsPendingEscalationPreviewAllowedAsync(caller.User, doc, ct))
            throw DashboardModerationException.NotActionable();

        try
        {
            return await _storage.CreateSignedUrlAsync(
                BucketName, doc.StoragePath, SignedUrlTtlSeconds, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> IsPendingEscalationPreviewAllowedAsync(User caller, Document document, CancellationToken ct)
    {
        if (document.Status != DocumentStatus.Ready || document.ReviewStatus != DocumentReviewStatus.Escalated)
            return false;

        var isAdmin = await _context.Roles.AsNoTracking()
            .AnyAsync(role => role.Id == caller.RoleId && role.RoleName == Role.AdminRoleName, ct);
        if (!isAdmin)
            return false;

        return await _context.DocumentEscalationItems.AsNoTracking().AnyAsync(item =>
            item.DocumentId == document.Id
            && item.DocumentModerationGeneration == document.ModerationGeneration
            && item.ResolutionStatus == "Pending"
            && item.Escalation.EscalationStatus == "Pending"
            && item.Escalation.FolderId == document.FolderId,
            ct);
    }

    public async Task<UserAnalyticsDto> GetModerationAnalyticsAsync(Guid supabaseUserId, Guid? folderId, int page, int pageSize, CancellationToken ct)
    {
        await EnsureModeratorAsync(supabaseUserId, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<Document> query = _context.Documents.AsNoTracking()
            .Include(d => d.Folder)
            .Where(d => d.Folder != null
                && (d.Folder.ShareStatus == FolderStatus.PendingShare || d.Folder.ShareStatus == FolderStatus.Approved));
        if (folderId.HasValue)
            query = query.Where(d => d.FolderId == folderId.Value);

        var totalDocuments = await query.CountAsync(ct);
        var approvedDocuments = await query.Where(d => d.ReviewStatus == DocumentReviewStatus.Approved).CountAsync(ct);
        var pendingUnreviewedCount = await query.Where(d => d.ReviewStatus == DocumentReviewStatus.None).CountAsync(ct);
        var rejectedDocumentCount = await query.Where(d => d.ReviewStatus == DocumentReviewStatus.Rejected).CountAsync(ct);
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
                _context.DocumentChunks.Count(chunk => chunk.DocumentId == d.Id),
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
            PageSize: pageSize,
            PendingUnreviewedCount: pendingUnreviewedCount,
            RejectedDocumentCount: rejectedDocumentCount,
            AllDocumentsApproved: totalDocuments > 0 && approvedDocuments == totalDocuments
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

    private async Task<AuthorizedReviewer> EnsureModeratorAsync(Guid supabaseUserId, CancellationToken ct)
    {
        var caller = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.SupabaseUserId == supabaseUserId, ct);

        if (caller is null || !caller.IsActive)
        {
            throw DashboardModerationException.Forbidden();
        }

        var roleName = await _context.Roles
            .AsNoTracking()
            .Where(role => role.Id == caller.RoleId)
            .Select(role => role.RoleName)
            .FirstOrDefaultAsync(ct);
        if (!string.Equals(roleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(roleName, Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw DashboardModerationException.Forbidden();
        }

        return new AuthorizedReviewer(caller, roleName!);
    }

    private async Task<Document> GetActionableModerationDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var document = await _context.Documents
            .Include(item => item.Folder)
            .FirstOrDefaultAsync(item => item.Id == documentId, ct);

        if (document is null)
            throw DashboardModerationException.NotFound();
        if (document.Folder is null || document.FolderId != document.Folder.Id || document.UserId != document.Folder.UserId
            || document.Folder.ShareStatus is not (FolderStatus.PendingShare or FolderStatus.Approved)
            || document.Status != DocumentStatus.Ready
            || document.ReviewStatus != DocumentReviewStatus.None)
            throw DashboardModerationException.NotActionable();

        return document;
    }

    private sealed record AuthorizedReviewer(User User, string RoleLabel);
}

public sealed class DashboardModerationException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    private DashboardModerationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public static DashboardModerationException Forbidden() =>
        new(StatusCodes.Status403Forbidden, "moderation_forbidden", "An active Admin or Moderator account is required.");

    public static DashboardModerationException NotFound() =>
        new(StatusCodes.Status404NotFound, "document_not_found", "Document not found.");

    public static DashboardModerationException NotActionable() =>
        new(StatusCodes.Status409Conflict, "document_not_pending_share", "Document is not in a pending-share folder.");

    public static DashboardModerationException ReasonTooLong(int maxLength) =>
        new(StatusCodes.Status400BadRequest, "reason_too_long", $"Reason must be {maxLength:N0} characters or fewer.");

}
