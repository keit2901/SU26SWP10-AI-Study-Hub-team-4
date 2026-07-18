namespace AI_Study_Hub_v2.Dtos;

public sealed record StorageQuotaSnapshotDto(
    long UsedBytes,
    long? QuotaBytes,
    string PlanKey,
    string PlanDisplayName,
    DateTimeOffset? ExpiresAt = null);
