namespace AI_Study_Hub_v2.Data.Entities;

public sealed class PaymentTransaction
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? UserPlanId { get; set; }

    public string TxnRef { get; set; } = string.Empty;

    public string PlanKey { get; set; } = string.Empty;

    public string BillingCycle { get; set; } = string.Empty;

    public long AmountVnd { get; set; }

    public string Status { get; set; } = "pending";

    public string? VnpayResponseJson { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation
    public User User { get; set; } = null!;

    public Plan Plan { get; set; } = null!;

    public UserPlan? UserPlan { get; set; }
}
