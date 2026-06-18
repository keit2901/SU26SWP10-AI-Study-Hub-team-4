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

        builder.Property(f => f.IsShared)
            .HasColumnName("is_shared")
            .HasDefaultValue(false);

        builder.Property(f => f.SharedAt)
            .HasColumnName("shared_at");

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
