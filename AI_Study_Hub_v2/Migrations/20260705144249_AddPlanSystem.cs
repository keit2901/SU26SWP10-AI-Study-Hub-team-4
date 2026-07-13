using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "storage_used_bytes",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Backfill storage_used_bytes from existing documents.
            migrationBuilder.Sql(@"
                UPDATE users u
                SET storage_used_bytes = COALESCE(
                    (SELECT SUM(d.file_size_bytes) FROM documents d WHERE d.user_id = u.id), 0
                )
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS benchmark_results (
                    id uuid NOT NULL DEFAULT (gen_random_uuid()),
                    model_name character varying(100) NOT NULL,
                    provider character varying(50) NOT NULL,
                    run_at timestamp with time zone NOT NULL,
                    overall_score double precision NOT NULL,
                    citation_accuracy double precision NOT NULL,
                    hallucination_rate double precision NOT NULL,
                    refusal_accuracy double precision NOT NULL,
                    tutoring_quality double precision NOT NULL,
                    diagram_accuracy double precision NOT NULL,
                    p50_latency_ms bigint NOT NULL,
                    p95_latency_ms bigint NOT NULL,
                    total_questions integer NOT NULL,
                    passed_questions integer NOT NULL,
                    failed_questions integer NOT NULL,
                    is_automated boolean NOT NULL,
                    alert_triggered boolean NOT NULL,
                    payload_json jsonb NOT NULL DEFAULT ('{}'::jsonb),
                    created_at timestamp with time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
                    CONSTRAINT ""PK_benchmark_results"" PRIMARY KEY (id)
                );
            ");

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    plan_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    storage_quota_bytes = table.Column<long>(type: "bigint", nullable: true),
                    max_document_count = table.Column<int>(type: "integer", nullable: true),
                    max_folder_count = table.Column<int>(type: "integer", nullable: true),
                    daily_token_quota = table.Column<long>(type: "bigint", nullable: true),
                    max_file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    max_docs_per_folder = table.Column<int>(type: "integer", nullable: true),
                    monthly_price_vnd = table.Column<long>(type: "bigint", nullable: true),
                    yearly_price_vnd = table.Column<long>(type: "bigint", nullable: true),
                    feature_flags_json = table.Column<string>(type: "jsonb", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_plans_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_plans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    txn_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    plan_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount_vnd = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vnpay_response_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "FK_payment_transactions_user_plans_user_plan_id",
                        column: x => x.user_plan_id,
                        principalTable: "user_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payment_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_benchmark_results_is_automated
                ON benchmark_results (is_automated);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_benchmark_results_model_run_at
                ON benchmark_results (model_name, run_at);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_benchmark_results_run_at
                ON benchmark_results (run_at);
            ");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_txn_ref",
                table: "payment_transactions",
                column: "txn_ref");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_user_id",
                table: "payment_transactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_user_plan_id",
                table: "payment_transactions",
                column: "user_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_plans_plan_key",
                table: "plans",
                column: "plan_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_plans_plan_id",
                table: "user_plans",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_plans_user_id_status",
                table: "user_plans",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "benchmark_results");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "user_plans");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropColumn(
                name: "storage_used_bytes",
                table: "users");
        }
    }
}
