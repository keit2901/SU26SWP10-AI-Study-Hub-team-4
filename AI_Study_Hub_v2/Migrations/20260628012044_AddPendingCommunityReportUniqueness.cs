using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingCommunityReportUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_community_reports_pending_folder_reporter",
                table: "community_reports",
                columns: new[] { "folder_id", "reported_by_user_id" },
                unique: true,
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_community_reports_pending_folder_reporter",
                table: "community_reports");
        }
    }
}
