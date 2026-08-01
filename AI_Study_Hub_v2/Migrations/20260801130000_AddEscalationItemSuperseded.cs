using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801130000_AddEscalationItemSuperseded")]
    /// <inheritdoc />
    public partial class AddEscalationItemSuperseded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_escalation_items_resolution_status",
                table: "document_escalation_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_escalation_items_resolution_status",
                table: "document_escalation_items",
                sql: "resolution_status IN ('Pending', 'Approved', 'Rejected', 'Superseded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Superseded is not valid in the legacy constraint; retain an explicit audit suffix.
            migrationBuilder.Sql("""
                UPDATE document_escalation_items
                SET resolution_status = 'Rejected',
                    admin_response = CASE
                        WHEN admin_response IS NULL OR admin_response = '' THEN 'Legacy downgrade mapped Superseded to Rejected.'
                        ELSE left(
                            admin_response,
                            2000 - char_length(E'\n\nLegacy downgrade mapped Superseded to Rejected.'))
                            || E'\n\nLegacy downgrade mapped Superseded to Rejected.'
                    END
                WHERE resolution_status = 'Superseded';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_escalation_items_resolution_status",
                table: "document_escalation_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_escalation_items_resolution_status",
                table: "document_escalation_items",
                sql: "resolution_status IN ('Pending', 'Approved', 'Rejected')");
        }
    }
}
