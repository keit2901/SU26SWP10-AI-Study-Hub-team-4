using System.Collections.Generic;
using AI_Study_Hub_v2.Components.Pages.Dashboard;

namespace AI_Study_Hub_v2.Dtos;

public record AdminDashboardStatsDto(
    int TotalUsers,
    int TotalDocuments,
    long TotalStorageUsedMb,
    int TotalActiveSessions,
    int TotalFailedJobs,
    int IndexedCount,
    int ProcessingCount,
    int PendingCount,
    long DailyTokensUsed,
    long DailyTokenQuota
);

public record UserDashboardStatsDto(
    int TotalFolders,
    int TotalDocuments,
    long StorageUsedMb,
    int ApprovedDocuments,
    int PendingDocuments,
    int RejectedDocuments,
    List<FolderViewModel> RecentFolders
);

public record DashboardSubjectDto(
    string SubjectCode,
    int DocumentCount,
    double StorageUsedMb,
    System.DateTimeOffset? LatestUploadDate
);

public record DashboardSemesterDto(
    string Semester,
    int DocumentCount,
    double StorageUsedMb,
    System.DateTimeOffset? LatestUploadDate
);

public record UserAnalyticsDto(
    int TotalDocuments,
    double CompletionRate,
    double AvgProcessingTimeHrs,
    double StorageUsedMb,
    System.Collections.Generic.List<double> DailyUploadCounts,
    System.Collections.Generic.List<string> DailyUploadLabels,
    System.Collections.Generic.List<double> DailyApprovedCounts,
    System.Collections.Generic.List<double> DailyRejectedCounts,
    System.Collections.Generic.List<AnalyticsIssueDto> CommonIssues,
    System.Collections.Generic.List<AnalyticsDocumentDto> RecentDocuments,
    int TotalDocumentCount = 0,
    int Page = 1,
    int PageSize = 0
);

public record AnalyticsIssueDto(
    string ErrorMessage,
    int IncidentCount
);

public record AnalyticsDocumentDto(
    System.Guid Id,
    string Name,
    string Status,
    System.DateTimeOffset CreatedAt,
    int? PageCount,
    int ChunkCount,
    string? FolderName = null,
    System.DateTimeOffset? FolderSharedAt = null
);

public record ActivityTrendPoint(
    string Label,
    int Uploads,
    int Documents,
    int Failed
);

public record ActivityTrendsDto(
    string Period,
    System.Collections.Generic.List<ActivityTrendPoint> Points
);
