using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.ChatSessionId)
            .HasColumnName("chat_session_id")
            .IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Content)
            .HasColumnName("content")
            .IsRequired();

        builder.Property(m => m.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb");

        builder.Property(m => m.SequenceNumber)
            .HasColumnName("sequence_number")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(m => m.ChatSessionId);
        builder.HasIndex(m => new { m.ChatSessionId, m.SequenceNumber }).IsUnique();

        builder.HasOne(m => m.ChatSession)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
