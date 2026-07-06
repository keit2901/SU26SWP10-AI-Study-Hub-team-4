using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    public partial class AddPhase3BenchmarkHistoryAndKeywordIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "benchmark_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    overall_score = table.Column<double>(type: "double precision", nullable: false),
                    citation_accuracy = table.Column<double>(type: "double precision", nullable: false),
                    hallucination_rate = table.Column<double>(type: "double precision", nullable: false),
                    refusal_accuracy = table.Column<double>(type: "double precision", nullable: false),
                    tutoring_quality = table.Column<double>(type: "double precision", nullable: false),
                    diagram_accuracy = table.Column<double>(type: "double precision", nullable: false),
                    p50_latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    p95_latency_ms = table.Column<long>(type: "bigint", nullable: false),
                    total_questions = table.Column<int>(type: "integer", nullable: false),
                    passed_questions = table.Column<int>(type: "integer", nullable: false),
                    failed_questions = table.Column<int>(type: "integer", nullable: false),
                    is_automated = table.Column<bool>(type: "boolean", nullable: false),
                    alert_triggered = table.Column<bool>(type: "boolean", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_benchmark_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_benchmark_results_is_automated",
                table: "benchmark_results",
                column: "is_automated");

            migrationBuilder.CreateIndex(
                name: "ix_benchmark_results_model_run_at",
                table: "benchmark_results",
                columns: new[] { "model_name", "run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_benchmark_results_run_at",
                table: "benchmark_results",
                column: "run_at");

            migrationBuilder.Sql("""
                ALTER TABLE document_chunks
                ADD COLUMN IF NOT EXISTS search_vector tsvector
                GENERATED ALWAYS AS (to_tsvector('simple', coalesce(content, ''))) STORED;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_document_chunks_search_vector
                ON document_chunks USING GIN (search_vector);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_document_chunks_search_vector;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE document_chunks
                DROP COLUMN IF EXISTS search_vector;
                """);

            migrationBuilder.DropTable(
                name: "benchmark_results");
        }
    }
}
