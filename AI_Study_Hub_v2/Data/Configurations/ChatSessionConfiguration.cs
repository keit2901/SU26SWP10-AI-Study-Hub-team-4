using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("chat_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(s => s.FolderId)
            .HasColumnName("folder_id");

        builder.Property(s => s.Title)
            .HasColumnName("title")
            .HasMaxLength(200);

        builder.Property(s => s.Model)
            .HasColumnName("model")
            .HasMaxLength(50);

        builder.Property(s => s.TopK)
            .HasColumnName("top_k")
            .HasDefaultValue(5);

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => new { s.UserId, s.UpdatedAt }).IsDescending(false, true);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Folder)
            .WithMany()
            .HasForeignKey(s => s.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
