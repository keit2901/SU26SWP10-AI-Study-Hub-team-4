using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentEscalation : Migration
    {
        /// <inheritdoc />
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

            migrationBuilder.CreateTable(
                name: "document_escalations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    escalation_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    admin_response = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_escalations", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_escalations_folders_folder_id",
                        column: x => x.folder_id,
                        principalTable: "folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_escalations_users_escalated_by_user_id",
                        column: x => x.escalated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_document_escalations_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "document_escalation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    escalation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reject_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_escalation_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_escalation_items_document_escalations_escalation_id",
                        column: x => x.escalation_id,
                        principalTable: "document_escalations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_escalation_items_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id");
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

            migrationBuilder.CreateIndex(
                name: "IX_document_escalation_items_document_id",
                table: "document_escalation_items",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_escalation_items_escalation_id",
                table: "document_escalation_items",
                column: "escalation_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_escalations_escalated_by_user_id",
                table: "document_escalations",
                column: "escalated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_escalations_escalation_status",
                table: "document_escalations",
                column: "escalation_status");

            migrationBuilder.CreateIndex(
                name: "IX_document_escalations_folder_id",
                table: "document_escalations",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_escalations_resolved_by_user_id",
                table: "document_escalations",
                column: "resolved_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benchmark_results");

            migrationBuilder.DropTable(
                name: "document_escalation_items");

            migrationBuilder.DropTable(
                name: "document_escalations");
        }
    }
}
