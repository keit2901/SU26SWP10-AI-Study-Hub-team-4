using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingModelToDocumentChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "embedding_model",
                table: "document_chunks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("""
                CREATE INDEX ix_document_chunks_embedding_model
                ON document_chunks 
                USING ivfflat (embedding vector_cosine_ops) 
                WHERE embedding_model = 'all-minilm:l6-v2';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_document_chunks_embedding_model;
                """);

            migrationBuilder.DropColumn(
                name: "embedding_model",
                table: "document_chunks");
        }
    }
}