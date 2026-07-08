using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class UserPlanConfiguration : IEntityTypeConfiguration<UserPlan>
{
    public void Configure(EntityTypeBuilder<UserPlan> builder)
    {
        builder.ToTable("user_plans");

        builder.HasKey(up => up.Id);

        builder.Property(up => up.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(up => up.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(up => up.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        builder.Property(up => up.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(up => up.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(up => up.PaidAt)
            .HasColumnName("paid_at");

        builder.Property(up => up.AssignedAt)
            .HasColumnName("assigned_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(up => new { up.UserId, up.Status });

        builder.HasOne(up => up.User)
            .WithMany()
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(up => up.Plan)
            .WithMany()
            .HasForeignKey(up => up.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
