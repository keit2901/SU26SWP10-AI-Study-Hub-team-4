using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderShareStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new column first (with default 0 = None)
            migrationBuilder.AddColumn<int>(
                name: "share_status",
                table: "folders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Migrate existing data: is_shared=true → share_status=2 (Approved)
            migrationBuilder.Sql("""
                UPDATE folders
                SET share_status = 2
                WHERE is_shared = TRUE;
                """);

            // Drop the old column
            migrationBuilder.DropColumn(
                name: "is_shared",
                table: "folders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "share_status",
                table: "folders");

            migrationBuilder.AddColumn<bool>(
                name: "is_shared",
                table: "folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
