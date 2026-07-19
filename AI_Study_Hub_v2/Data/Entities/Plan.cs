namespace AI_Study_Hub_v2.Data.Entities;

public sealed class Plan
{
    public Guid Id { get; set; }

    public string PlanKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long? StorageQuotaBytes { get; set; }

    public int? MaxDocumentCount { get; set; }

    public int? MaxFolderCount { get; set; }

    public long? DailyTokenQuota { get; set; }

    public long? MaxFileSizeBytes { get; set; }

    public int? MaxDocsPerFolder { get; set; }

    public long? MonthlyPriceVnd { get; set; }

    public long? YearlyPriceVnd { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
