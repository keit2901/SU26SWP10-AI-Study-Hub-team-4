using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class BenchmarkRunRecordConfiguration : IEntityTypeConfiguration<BenchmarkRunRecord>
{
    public void Configure(EntityTypeBuilder<BenchmarkRunRecord> builder)
    {
        builder.ToTable("benchmark_results");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ModelName)
            .HasColumnName("model_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.RunAt)
            .HasColumnName("run_at")
            .IsRequired();

        builder.Property(x => x.OverallScore)
            .HasColumnName("overall_score");

        builder.Property(x => x.CitationAccuracy)
            .HasColumnName("citation_accuracy");

        builder.Property(x => x.HallucinationRate)
            .HasColumnName("hallucination_rate");

        builder.Property(x => x.RefusalAccuracy)
            .HasColumnName("refusal_accuracy");

        builder.Property(x => x.TutoringQuality)
            .HasColumnName("tutoring_quality");

        builder.Property(x => x.DiagramAccuracy)
            .HasColumnName("diagram_accuracy");

        builder.Property(x => x.P50LatencyMs)
            .HasColumnName("p50_latency_ms");

        builder.Property(x => x.P95LatencyMs)
            .HasColumnName("p95_latency_ms");

        builder.Property(x => x.TotalQuestions)
            .HasColumnName("total_questions");

        builder.Property(x => x.PassedQuestions)
            .HasColumnName("passed_questions");

        builder.Property(x => x.FailedQuestions)
            .HasColumnName("failed_questions");

        builder.Property(x => x.IsAutomated)
            .HasColumnName("is_automated");

        builder.Property(x => x.AlertTriggered)
            .HasColumnName("alert_triggered");

        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(x => x.RunAt)
            .HasDatabaseName("ix_benchmark_results_run_at");

        builder.HasIndex(x => new { x.ModelName, x.RunAt })
            .HasDatabaseName("ix_benchmark_results_model_run_at");

        builder.HasIndex(x => x.IsAutomated)
            .HasDatabaseName("ix_benchmark_results_is_automated");
    }
}
