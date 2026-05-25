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
                Description = "Quản trị viên hệ thống, có quyền điều phối nhân sự, kiểm duyệt tài liệu và thay đổi tham số cấu hình AI",
                CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
            },
            new Role
            {
                Id = 2,
                RoleName = Role.StudentRoleName,
                Description = "Sinh viên khai thác tài nguyên học tập cá nhân, thực hiện hội thoại RAG và tham gia kiểm tra ôn tập",
                CreatedAt = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero)
            });
    }
}
