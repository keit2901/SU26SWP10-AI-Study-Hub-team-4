using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI_Study_Hub_v2.Data.Configurations;

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ToTable("document_chunks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.DocumentId)
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(c => c.ChunkIndex)
            .HasColumnName("chunk_index")
            .IsRequired();

        builder.Property(c => c.PageNumber)
            .HasColumnName("page_number");

        builder.Property(c => c.Content)
            .HasColumnName("content")
            .IsRequired();

        builder.Property(c => c.TokenCount)
            .HasColumnName("token_count");

        builder.Property(c => c.SectionTitle)
            .HasColumnName("section_title");

        // Embedding dimension is locked at 384 (BGE-small-en-v1.5).
        // Pgvector.EntityFrameworkCore maps Pgvector.Vector to the pgvector "vector(N)" type.
        builder.Property(c => c.Embedding)
            .HasColumnName("embedding")
            .HasColumnType($"vector({DocumentChunk.EmbeddingDimension})")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(c => c.DocumentId);
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();

        // NOTE: ivfflat cosine index is added as a raw SQL step in the migration
        // (EF doesn't natively model HasMethod("ivfflat") with operator class).
        // See Migration AddDocumentSchema -> migrationBuilder.Sql(...).

        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
