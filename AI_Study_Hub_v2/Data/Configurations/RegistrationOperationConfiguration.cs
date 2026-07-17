using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class RegistrationOperationConfiguration : IEntityTypeConfiguration<RegistrationOperation>
{
    public void Configure(EntityTypeBuilder<RegistrationOperation> builder)
    {
        builder.ToTable("registration_operations", table =>
        {
            table.HasCheckConstraint("ck_registration_operations_status", "status IN ('Prepared', 'CreatingIdentity', 'IdentityConfirmed', 'FinalizingProfile', 'ProfileCommitted', 'Completed', 'CompensationRequired', 'Compensating', 'Compensated', 'Conflict', 'Expired')");
            table.HasCheckConstraint("ck_registration_operations_attempt_count_non_negative", "attempt_count >= 0");
            table.HasCheckConstraint("ck_registration_operations_lease_pair", "(lease_token IS NULL AND lease_expires_at IS NULL) OR (lease_token IS NOT NULL AND lease_expires_at IS NOT NULL)");
            table.HasCheckConstraint("ck_registration_operations_identity_required", "status NOT IN ('IdentityConfirmed', 'FinalizingProfile', 'ProfileCommitted', 'Completed', 'CompensationRequired', 'Compensating') OR identity_id IS NOT NULL");
            table.HasCheckConstraint("ck_registration_operations_id_non_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_registration_operations_profile_user_id_non_empty", "profile_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_registration_operations_identity_id_non_empty", "identity_id IS NULL OR identity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_registration_operations_lease_token_non_empty", "lease_token IS NULL OR lease_token <> '00000000-0000-0000-0000-000000000000'::uuid");
        });

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(operation => operation.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(255).IsRequired();
        builder.Property(operation => operation.Username).HasColumnName("username").HasMaxLength(15).IsRequired();
        builder.Property(operation => operation.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
        builder.Property(operation => operation.ProfileUserId).HasColumnName("profile_user_id").IsRequired();
        builder.Property(operation => operation.IdentityId).HasColumnName("identity_id");
        builder.Property(operation => operation.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(RegistrationOperation.Prepared).IsRequired();
        builder.Property(operation => operation.LeaseToken).HasColumnName("lease_token");
        builder.Property(operation => operation.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(operation => operation.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(operation => operation.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(operation => operation.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(100);
        builder.Property(operation => operation.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
        builder.Property(operation => operation.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
        builder.Property(operation => operation.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(operation => operation.ProfileUserId).IsUnique();
        builder.HasIndex(operation => operation.IdentityId).IsUnique().HasFilter("identity_id IS NOT NULL");
        builder.HasIndex(operation => operation.NormalizedEmail).IsUnique().HasFilter("status NOT IN ('Compensated', 'Conflict', 'Expired')");
        builder.HasIndex(operation => operation.Username).IsUnique().HasFilter("status NOT IN ('Compensated', 'Conflict', 'Expired')");
        builder.HasIndex(operation => new { operation.Status, operation.NextAttemptAt, operation.UpdatedAt });
        builder.HasIndex(operation => operation.LeaseExpiresAt);
    }
}
