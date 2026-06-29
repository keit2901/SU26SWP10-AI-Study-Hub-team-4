using System.Collections.Generic;
using AI_Study_Hub_v2.Components.Pages.Dashboard;

namespace AI_Study_Hub_v2.Dtos;

public record AdminDashboardStatsDto(
    int TotalUsers,
    int TotalDocuments,
    long TotalStorageUsedMb,
    int TotalActiveSessions,
    int TotalFailedJobs
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
    System.Collections.Generic.List<AnalyticsDocumentDto> RecentDocuments
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
    int ChunkCount
);
