using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(d => d.FolderId)
            .HasColumnName("folder_id");

        builder.Property(d => d.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(d => d.StoragePath)
            .HasColumnName("storage_path")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.FileSizeBytes)
            .HasColumnName("file_size_bytes")
            .IsRequired();

        builder.Property(d => d.MimeType)
            .HasColumnName("mime_type")
            .HasMaxLength(100)
            .IsRequired();

        // Sprint 1 SCRUM-12/15: required FPT subject + semester for upload + filter.
        builder.Property(d => d.SubjectCode)
            .HasColumnName("subject_code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.Semester)
            .HasColumnName("semester")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(d => d.PageCount)
            .HasColumnName("page_count");

        // ENUM mapping: maps Document.Status (.NET enum) <-> public.document_status (PG enum).
        // The PG enum is registered globally on the DbContext (see AppDbContext.OnModelCreating).
        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasColumnType("public.document_status")
            .HasDefaultValue(DocumentStatus.Uploading)
            .IsRequired();

        builder.Property(d => d.ReviewStatus)
            .HasColumnName("review_status")
            .HasDefaultValue(DocumentReviewStatus.None)
            .IsRequired();

        builder.Property(d => d.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(d => d.StoragePath).IsUnique();
        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.FolderId);
        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => new { d.SubjectCode, d.Semester })
            .HasDatabaseName("ix_documents_subject_semester");

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Folder)
            .WithMany(f => f.Documents)
            .HasForeignKey(d => d.FolderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
