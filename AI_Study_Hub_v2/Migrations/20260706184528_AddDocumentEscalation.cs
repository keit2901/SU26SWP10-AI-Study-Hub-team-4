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
            // benchmark_results table already exists in database — skip creation
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

            // benchmark_results indexes already exist — skip
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
            // benchmark_results table existed before — do not drop
            migrationBuilder.DropTable(
                name: "document_escalation_items");

            migrationBuilder.DropTable(
                name: "document_escalations");
        }
    }
}
