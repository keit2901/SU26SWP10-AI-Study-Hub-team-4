using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIngestionOperationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ingestion_operation_id",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_ingestion_operation_id",
                table: "documents",
                sql: "ingestion_operation_id IS NULL OR ingestion_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_documents_ingestion_operation_id",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ingestion_operation_id",
                table: "documents");
        }
    }
}
