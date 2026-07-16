using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedFolderCopyOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_folder_copy_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    destination_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reserved_storage_bytes = table.Column<long>(type: "bigint", nullable: false),
                    manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_folder_copy_operations", x => x.id);
                    table.CheckConstraint("ck_shared_folder_copy_operations_reserved_storage_non_negative", "reserved_storage_bytes >= 0");
                    table.ForeignKey(
                        name: "FK_shared_folder_copy_operations_users_destination_user_id",
                        column: x => x.destination_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shared_folder_copy_operations_destination_user_id",
                table: "shared_folder_copy_operations",
                column: "destination_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shared_folder_copy_operations_status",
                table: "shared_folder_copy_operations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_shared_folder_copy_operations_updated_at",
                table: "shared_folder_copy_operations",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_folder_copy_operations");
        }
    }
}
