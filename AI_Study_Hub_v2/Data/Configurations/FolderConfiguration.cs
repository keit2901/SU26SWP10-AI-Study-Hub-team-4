using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(f => f.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(f => f.Description)
            .HasColumnName("description");

        builder.Property(f => f.IsFavorite)
            .HasColumnName("is_favorite")
            .HasDefaultValue(false);

        builder.Property(f => f.ShareStatus)
            .HasColumnName("share_status")
            .HasDefaultValue(FolderStatus.None);

        builder.Property(f => f.SharedAt)
            .HasColumnName("shared_at");

        builder.Property(f => f.ShareReviewSource)
            .HasColumnName("share_review_source")
            .HasMaxLength(32);

        builder.Property(f => f.AiReviewReason)
            .HasColumnName("ai_review_reason")
            .HasMaxLength(2000);

        builder.Property(f => f.AiReviewConfidence)
            .HasColumnName("ai_review_confidence");

        builder.Property(f => f.AiReviewFailureCount)
            .HasColumnName("ai_review_failure_count")
            .HasDefaultValue(0);

        builder.Property(f => f.HumanReviewReason)
            .HasColumnName("human_review_reason")
            .HasMaxLength(2000);

        builder.Property(f => f.RequiresHumanReview)
            .HasColumnName("requires_human_review")
            .HasDefaultValue(false);

        builder.Property(f => f.AppealRequestedAt)
            .HasColumnName("appeal_requested_at");

        builder.Property(f => f.AppealMessage)
            .HasColumnName("appeal_message")
            .HasMaxLength(2000);
        builder.Property(f => f.Icon)
            .HasColumnName("icon")
            .HasMaxLength(50);

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => new { f.UserId, f.Name }).IsUnique();

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
