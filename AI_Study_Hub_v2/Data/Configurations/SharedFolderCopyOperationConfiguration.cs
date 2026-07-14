using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class SharedFolderCopyOperationConfiguration : IEntityTypeConfiguration<SharedFolderCopyOperation>
{
    public void Configure(EntityTypeBuilder<SharedFolderCopyOperation> builder)
    {
        builder.ToTable("shared_folder_copy_operations", table =>
        {
            table.HasCheckConstraint("ck_shared_folder_copy_operations_reserved_storage_non_negative",
                "reserved_storage_bytes >= 0");
        });

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(operation => operation.DestinationUserId).HasColumnName("destination_user_id").IsRequired();
        builder.Property(operation => operation.SourceFolderId).HasColumnName("source_folder_id").IsRequired();
        builder.Property(operation => operation.DestinationFolderId).HasColumnName("destination_folder_id").IsRequired();
        builder.Property(operation => operation.DestinationName).HasColumnName("destination_name").HasMaxLength(100).IsRequired();
        builder.Property(operation => operation.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.ReservedStorageBytes).HasColumnName("reserved_storage_bytes").IsRequired();
        builder.Property(operation => operation.ManifestJson).HasColumnName("manifest_json").HasColumnType("jsonb").IsRequired();
        builder.Property(operation => operation.LastError).HasColumnName("last_error").HasMaxLength(1000);
        builder.Property(operation => operation.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(operation => operation.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(operation => operation.DestinationUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(operation => operation.DestinationUserId).IsUnique();
        builder.HasIndex(operation => operation.Status);
        builder.HasIndex(operation => operation.UpdatedAt);
    }
}
