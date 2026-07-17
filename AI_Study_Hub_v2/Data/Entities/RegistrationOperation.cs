namespace AI_Study_Hub_v2.Data.Entities;

public sealed class RegistrationOperation
{
    public const string Prepared = "Prepared";
    public const string CreatingIdentity = "CreatingIdentity";
    public const string IdentityConfirmed = "IdentityConfirmed";
    public const string FinalizingProfile = "FinalizingProfile";
    public const string ProfileCommitted = "ProfileCommitted";
    public const string Completed = "Completed";
    public const string CompensationRequired = "CompensationRequired";
    public const string Compensating = "Compensating";
    public const string Compensated = "Compensated";
    public const string Conflict = "Conflict";
    public const string Expired = "Expired";

    public Guid Id { get; set; }
    public string NormalizedEmail { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Guid ProfileUserId { get; set; }
    public Guid? IdentityId { get; set; }
    public string Status { get; set; } = Prepared;
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
