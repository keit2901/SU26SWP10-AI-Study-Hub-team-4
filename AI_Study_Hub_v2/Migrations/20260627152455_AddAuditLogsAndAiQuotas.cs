using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260627152455_AddAuditLogsAndAiQuotas")]
public partial class AddAuditLogsAndAiQuotas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "daily_token_quota",
            table: "users",
            type: "bigint",
            nullable: false,
            defaultValue: 25_000L);

        migrationBuilder.AddColumn<DateOnly>(
            name: "token_usage_date",
            table: "users",
            type: "date",
            nullable: false,
            defaultValueSql: "CURRENT_DATE");

        migrationBuilder.AddColumn<long>(
            name: "tokens_used_today",
            table: "users",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "audit_logs",
            columns: table => new
            {
                id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false,
                    defaultValueSql: "gen_random_uuid()"),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false),
                entity_type = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false),
                entity_id = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: true),
                severity = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false,
                    defaultValue: "Low"),
                before_json = table.Column<string>(type: "jsonb", nullable: true),
                after_json = table.Column<string>(type: "jsonb", nullable: true),
                context_json = table.Column<string>(type: "jsonb", nullable: true),
                ip_address = table.Column<string>(
                    type: "character varying(64)",
                    maxLength: 64,
                    nullable: true),
                request_id = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: true),
                created_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_logs", item => item.id);
                table.ForeignKey(
                    name: "FK_audit_logs_users_actor_user_id",
                    column: item => item.actor_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_action_created_at",
            table: "audit_logs",
            columns: new[] { "action", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_actor_user_id",
            table: "audit_logs",
            column: "actor_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_audit_logs_created_at",
            table: "audit_logs",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "IX_community_reports_folder_id_reported_by_user_id",
            table: "community_reports",
            columns: new[] { "folder_id", "reported_by_user_id" },
            unique: true,
            filter: "status = 'Pending'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_community_reports_folder_id_reported_by_user_id",
            table: "community_reports");

        migrationBuilder.DropTable(name: "audit_logs");

        migrationBuilder.DropColumn(
            name: "daily_token_quota",
            table: "users");

        migrationBuilder.DropColumn(
            name: "token_usage_date",
            table: "users");

        migrationBuilder.DropColumn(
            name: "tokens_used_today",
            table: "users");
    }
}
