using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.PlanKey)
            .HasColumnName("plan_key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description");

        builder.Property(p => p.StorageQuotaBytes)
            .HasColumnName("storage_quota_bytes");

        builder.Property(p => p.MaxDocumentCount)
            .HasColumnName("max_document_count");

        builder.Property(p => p.MaxFolderCount)
            .HasColumnName("max_folder_count");

        builder.Property(p => p.DailyTokenQuota)
            .HasColumnName("daily_token_quota");

        builder.Property(p => p.MaxFileSizeBytes)
            .HasColumnName("max_file_size_bytes");

        builder.Property(p => p.MaxDocsPerFolder)
            .HasColumnName("max_docs_per_folder");

        builder.Property(p => p.MonthlyPriceVnd)
            .HasColumnName("monthly_price_vnd");

        builder.Property(p => p.YearlyPriceVnd)
            .HasColumnName("yearly_price_vnd");

        builder.Property(p => p.FeatureFlagsJson)
            .HasColumnName("feature_flags_json")
            .HasColumnType("jsonb");

        builder.Property(p => p.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(p => p.PlanKey).IsUnique();
    }
}
