using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("payment_transactions", t =>
        {
            // W1.1: CHECK constraint — amount must be non-negative
            t.HasCheckConstraint("ck_payment_transactions_amount_non_negative",
                "amount_vnd >= 0");

            // W1.2: CHECK constraints on status and billing_cycle
            t.HasCheckConstraint("ck_payment_transactions_status",
                "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired')");
            t.HasCheckConstraint("ck_payment_transactions_billing_cycle",
                "billing_cycle IN ('monthly', 'yearly')");
        });

        builder.HasKey(pt => pt.Id);

        builder.Property(pt => pt.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(pt => pt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(pt => pt.UserPlanId)
            .HasColumnName("user_plan_id");

        builder.Property(pt => pt.TxnRef)
            .HasColumnName("txn_ref")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(pt => pt.PlanKey)
            .HasColumnName("plan_key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pt => pt.BillingCycle)
            .HasColumnName("billing_cycle")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pt => pt.AmountVnd)
            .HasColumnName("amount_vnd")
            .IsRequired();

        builder.Property(pt => pt.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pt => pt.VnpayResponseJson)
            .HasColumnName("vnpay_response_json")
            .HasColumnType("jsonb");

        builder.Property(pt => pt.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(pt => pt.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(pt => pt.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(pt => pt.ExpiresAt)
            .HasColumnName("expires_at");

        builder.HasIndex(pt => new { pt.TxnRef, pt.UserId }).IsUnique();

        builder.HasIndex(pt => new { pt.Status, pt.CreatedAt })
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_payment_transactions_status_created_at_pending");

        // W5.1: change User cascade delete → Restrict
        builder.HasOne(pt => pt.User)
            .WithMany()
            .HasForeignKey(pt => pt.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // F1.2: FK from payment_transactions.plan_key to plans.plan_key
        builder.HasOne(pt => pt.Plan)
            .WithMany()
            .HasForeignKey(pt => pt.PlanKey)
            .HasPrincipalKey(p => p.PlanKey)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pt => pt.UserPlan)
            .WithMany()
            .HasForeignKey(pt => pt.UserPlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
