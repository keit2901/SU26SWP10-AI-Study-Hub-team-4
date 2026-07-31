using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("user_notifications", table =>
        {
            table.HasCheckConstraint("ck_user_notifications_kind", "kind = 'FolderModerationFinal'");
            table.HasCheckConstraint("ck_user_notifications_outcome", "outcome IN ('Approved', 'Rejected')");
            table.HasCheckConstraint("ck_user_notifications_submission_number", "submission_number >= 0");
        });

        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(notification => notification.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(notification => notification.FolderId).HasColumnName("folder_id").IsRequired();
        builder.Property(notification => notification.SubmissionNumber).HasColumnName("submission_number").IsRequired();
        builder.Property(notification => notification.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(notification => notification.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(notification => notification.FolderName).HasColumnName("folder_name").HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(notification => notification.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
        builder.Property(notification => notification.ReadAt).HasColumnName("read_at");

        builder.HasIndex(notification => new { notification.RecipientUserId, notification.FolderId, notification.SubmissionNumber }).IsUnique();
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.CreatedAt })
            .HasDatabaseName("ix_user_notifications_recipient_created_at");
        builder.HasIndex(notification => notification.RecipientUserId)
            .HasDatabaseName("ix_user_notifications_recipient_unread")
            .HasFilter("read_at IS NULL");

        builder.HasOne(notification => notification.RecipientUser).WithMany().HasForeignKey(notification => notification.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(notification => notification.Folder).WithMany().HasForeignKey(notification => notification.FolderId).OnDelete(DeleteBehavior.Cascade);
    }
}
