using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class FolderReactionConfiguration : IEntityTypeConfiguration<FolderReaction>
{
    public void Configure(EntityTypeBuilder<FolderReaction> builder)
    {
        builder.ToTable("folder_reactions");

        builder.HasKey(fr => new { fr.FolderId, fr.UserId });

        builder.Property(fr => fr.FolderId)
            .HasColumnName("folder_id")
            .IsRequired();

        builder.Property(fr => fr.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(fr => fr.IsLike)
            .HasColumnName("is_like")
            .IsRequired();

        builder.Property(fr => fr.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(fr => fr.Folder)
            .WithMany(f => f.Reactions)
            .HasForeignKey(fr => fr.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(fr => fr.User)
            .WithMany()
            .HasForeignKey(fr => fr.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
