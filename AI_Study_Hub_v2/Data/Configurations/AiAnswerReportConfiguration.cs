using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class AiAnswerReportConfiguration : IEntityTypeConfiguration<AiAnswerReport>
{
    public void Configure(EntityTypeBuilder<AiAnswerReport> builder)
    {
        builder.ToTable("ai_answer_reports");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(r => r.Question)
            .HasColumnName("question")
            .IsRequired();

        builder.Property(r => r.Answer)
            .HasColumnName("answer")
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(r => r.Details)
            .HasColumnName("details");

        builder.Property(r => r.ContextJson)
            .HasColumnName("context_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(r => r.SourcesJson)
            .HasColumnName("sources_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .HasDefaultValue("open")
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CreatedAt);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
