using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class DocumentEscalationConfiguration : IEntityTypeConfiguration<DocumentEscalation>
{
    public void Configure(EntityTypeBuilder<DocumentEscalation> builder)
    {
        builder.ToTable("document_escalations", table =>
        {
            table.HasCheckConstraint("ck_document_escalations_status", "escalation_status IN ('Pending', 'Resolved')");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.FolderId).HasColumnName("folder_id").IsRequired();
        builder.Property(e => e.EscalatedByUserId).HasColumnName("escalated_by_user_id").IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.EscalationStatus).HasColumnName("escalation_status").HasMaxLength(50).HasDefaultValue("Pending");
        builder.Property(e => e.AdminResponse).HasColumnName("admin_response").HasMaxLength(2000);
        builder.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(e => e.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(e => e.FolderId);
        builder.HasIndex(e => e.EscalationStatus);

        builder.HasOne(e => e.Folder).WithMany().HasForeignKey(e => e.FolderId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.EscalatedByUser).WithMany().HasForeignKey(e => e.EscalatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.ResolvedByUser).WithMany().HasForeignKey(e => e.ResolvedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class DocumentEscalationItemConfiguration : IEntityTypeConfiguration<DocumentEscalationItem>
{
    public void Configure(EntityTypeBuilder<DocumentEscalationItem> builder)
    {
        builder.ToTable("document_escalation_items", table =>
        {
            table.HasCheckConstraint("ck_document_escalation_items_generation_non_negative", "document_moderation_generation >= 0");
            table.HasCheckConstraint("ck_document_escalation_items_resolution_status", "resolution_status IN ('Pending', 'Approved', 'Rejected')");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.EscalationId).HasColumnName("escalation_id").IsRequired();
        builder.Property(e => e.DocumentId).HasColumnName("document_id");
        builder.Property(e => e.DocumentFileName).HasColumnName("document_file_name").HasMaxLength(255).IsRequired();
        builder.Property(e => e.DocumentModerationGeneration).HasColumnName("document_moderation_generation").HasDefaultValue(0).IsRequired();
        builder.Property(e => e.RejectReason).HasColumnName("reject_reason").HasMaxLength(2000).IsRequired();
        builder.Property(e => e.ResolutionStatus).HasColumnName("resolution_status").HasMaxLength(16).HasDefaultValue("Pending").IsRequired();
        builder.Property(e => e.AdminResponse).HasColumnName("admin_response").HasMaxLength(2000);
        builder.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(e => e.ResolvedAt).HasColumnName("resolved_at");

        builder.HasIndex(e => e.EscalationId);
        builder.HasIndex(e => new { e.ResolutionStatus, e.DocumentModerationGeneration })
            .HasDatabaseName("ix_document_escalation_items_status_generation");
        builder.HasIndex(e => e.DocumentId)
            .HasDatabaseName("ux_document_escalation_items_pending_document")
            .IsUnique()
            .HasFilter("document_id IS NOT NULL AND resolution_status = 'Pending'");

        builder.HasOne(e => e.Escalation).WithMany(e => e.Items).HasForeignKey(e => e.EscalationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Document).WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.ResolvedByUser).WithMany().HasForeignKey(e => e.ResolvedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
