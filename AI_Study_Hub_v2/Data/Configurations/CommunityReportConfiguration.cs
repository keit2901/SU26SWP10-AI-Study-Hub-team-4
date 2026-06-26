using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class CommunityReportConfiguration : IEntityTypeConfiguration<CommunityReport>
{
    public void Configure(EntityTypeBuilder<CommunityReport> builder)
    {
        builder.ToTable("community_reports");

        builder.HasKey(cr => cr.Id);

        builder.Property(cr => cr.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(cr => cr.FolderId)
            .HasColumnName("folder_id")
            .IsRequired();

        builder.Property(cr => cr.ReportedByUserId)
            .HasColumnName("reported_by_user_id")
            .IsRequired();

        builder.Property(cr => cr.Reason)
            .HasColumnName("reason")
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(cr => cr.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(cr => cr.Resolution)
            .HasColumnName("resolution")
            .HasMaxLength(2000);

        builder.Property(cr => cr.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id");

        builder.Property(cr => cr.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(cr => cr.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.HasOne(cr => cr.Folder)
            .WithMany()
            .HasForeignKey(cr => cr.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cr => cr.ReportedBy)
            .WithMany()
            .HasForeignKey(cr => cr.ReportedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cr => cr.ResolvedBy)
            .WithMany()
            .HasForeignKey(cr => cr.ResolvedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
