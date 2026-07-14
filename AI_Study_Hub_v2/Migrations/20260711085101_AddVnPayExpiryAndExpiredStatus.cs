using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddVnPayExpiryAndExpiredStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_transactions_status",
                table: "payment_transactions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "payment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_status_created_at_pending",
                table: "payment_transactions",
                columns: new[] { "status", "created_at" },
                filter: "status = 'pending'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_transactions_status",
                table: "payment_transactions",
                sql: "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_status_created_at_pending",
                table: "payment_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_transactions_status",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "payment_transactions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_transactions_status",
                table: "payment_transactions",
                sql: "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded')");
        }
    }
}
