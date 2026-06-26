using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.ToTable("quizzes");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(q => q.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(q => q.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(q => q.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .HasDefaultValue("Quiz");

        builder.Property(q => q.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue(QuizStatus.InProgress)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(q => q.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(50);

        builder.Property(q => q.CurrentQuestionIndex)
            .HasColumnName("current_question_index")
            .HasDefaultValue(0);

        builder.Property(q => q.TotalQuestions)
            .HasColumnName("total_questions")
            .HasDefaultValue(8);

        builder.Property(q => q.QuestionsJson)
            .HasColumnName("questions_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(q => q.AnswersJson)
            .HasColumnName("answers_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(q => q.SubmittedJson)
            .HasColumnName("submitted_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(q => q.ScopeJson)
            .HasColumnName("scope_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(q => q.Score)
            .HasColumnName("score");

        builder.Property(q => q.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(q => q.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(q => q.SessionId);
        builder.HasIndex(q => q.UserId);
        builder.HasIndex(q => q.Status);

        builder.HasOne(q => q.Session)
            .WithMany()
            .HasForeignKey(q => q.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(q => q.User)
            .WithMany()
            .HasForeignKey(q => q.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
