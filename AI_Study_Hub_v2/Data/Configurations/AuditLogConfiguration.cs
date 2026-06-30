using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(log => log.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(log => log.Action)
            .HasColumnName("action")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(log => log.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(log => log.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(200);
        builder.Property(log => log.Severity)
            .HasColumnName("severity")
            .HasMaxLength(20)
            .HasDefaultValue("Low")
            .IsRequired();
        builder.Property(log => log.BeforeJson)
            .HasColumnName("before_json")
            .HasColumnType("jsonb");
        builder.Property(log => log.AfterJson)
            .HasColumnName("after_json")
            .HasColumnType("jsonb");
        builder.Property(log => log.ContextJson)
            .HasColumnName("context_json")
            .HasColumnType("jsonb");
        builder.Property(log => log.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(64);
        builder.Property(log => log.RequestId)
            .HasColumnName("request_id")
            .HasMaxLength(100);
        builder.Property(log => log.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(log => log.CreatedAt);
        builder.HasIndex(log => new { log.Action, log.CreatedAt });
        builder.HasIndex(log => log.ActorUserId);

        builder.HasOne(log => log.ActorUser)
            .WithMany()
            .HasForeignKey(log => log.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
