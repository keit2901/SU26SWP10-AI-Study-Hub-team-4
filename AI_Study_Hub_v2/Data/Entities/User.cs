namespace AI_Study_Hub_v2.Data.Entities;

public sealed class User
{
    public Guid Id { get; set; }

    public int RoleId { get; set; }

    /// <summary>
    /// Foreign key into the Supabase Auth-managed <c>auth.users</c> table.
    /// Identity (email, password, refresh tokens) is owned by GoTrue.
    /// </summary>
    public Guid SupabaseUserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public long TotalTokensUsed { get; set; }

    public long StorageUsedBytes { get; set; }

    public long DailyTokenQuota { get; set; } = 25_000;

    public long TokensUsedToday { get; set; }

    public DateOnly TokenUsageDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Role Role { get; set; } = null!;
}
