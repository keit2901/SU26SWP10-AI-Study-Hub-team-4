using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "registration_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    username = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    profile_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Prepared"),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_operations", x => x.id);
                    table.CheckConstraint("ck_registration_operations_attempt_count_non_negative", "attempt_count >= 0");
                    table.CheckConstraint("ck_registration_operations_id_non_empty", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_registration_operations_identity_id_non_empty", "identity_id IS NULL OR identity_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_registration_operations_identity_required", "status NOT IN ('IdentityConfirmed', 'FinalizingProfile', 'ProfileCommitted', 'Completed', 'CompensationRequired', 'Compensating') OR identity_id IS NOT NULL");
                    table.CheckConstraint("ck_registration_operations_lease_pair", "(lease_token IS NULL AND lease_expires_at IS NULL) OR (lease_token IS NOT NULL AND lease_expires_at IS NOT NULL)");
                    table.CheckConstraint("ck_registration_operations_lease_token_non_empty", "lease_token IS NULL OR lease_token <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_registration_operations_profile_user_id_non_empty", "profile_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_registration_operations_status", "status IN ('Prepared', 'CreatingIdentity', 'IdentityConfirmed', 'FinalizingProfile', 'ProfileCommitted', 'Completed', 'CompensationRequired', 'Compensating', 'Compensated', 'Conflict', 'Expired')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_identity_id",
                table: "registration_operations",
                column: "identity_id",
                unique: true,
                filter: "identity_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_lease_expires_at",
                table: "registration_operations",
                column: "lease_expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_normalized_email",
                table: "registration_operations",
                column: "normalized_email",
                unique: true,
                filter: "status NOT IN ('Compensated', 'Conflict', 'Expired')");

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_profile_user_id",
                table: "registration_operations",
                column: "profile_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_status_next_attempt_at_updated_at",
                table: "registration_operations",
                columns: new[] { "status", "next_attempt_at", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_registration_operations_username",
                table: "registration_operations",
                column: "username",
                unique: true,
                filter: "status NOT IN ('Compensated', 'Conflict', 'Expired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registration_operations");

        }
    }
}
