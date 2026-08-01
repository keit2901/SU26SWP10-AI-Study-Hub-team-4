using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(r => r.RoleName)
            .HasColumnName("role_name")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(r => r.RoleName).IsUnique();

        builder.HasData(
            new Role
            {
                Id = 1,
                RoleName = Role.AdminRoleName,
                Description = "System administrator responsible for managing users, moderating documents, and configuring AI settings.",
                CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
            },
            new Role
            {
                Id = 2,
                RoleName = Role.StudentRoleName,
                Description = "Student who uses personal learning resources, participates in RAG conversations, and completes review quizzes.",
                CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
            },
            new Role
            {
                Id = 3,
                RoleName = Role.ModeratorRoleName,
                Description = "Community moderator who reviews and handles violation reports without access to system settings or user management.",
                CreatedAt = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero)
            });
    }
}
