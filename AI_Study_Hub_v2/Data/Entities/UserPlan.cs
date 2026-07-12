namespace AI_Study_Hub_v2.Data.Entities;

public sealed class UserPlan
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public string Status { get; set; } = "active";

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;

    public Plan Plan { get; set; } = null!;
}
