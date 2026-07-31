using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPerFileModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "moderation_generation",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Preserve folders approved before per-file review status was introduced.
            migrationBuilder.Sql("""
                UPDATE documents AS d
                SET review_status = 1
                FROM folders AS f
                WHERE d.folder_id = f.id
                  AND f.share_status = 2
                  AND d.status = 'ready'
                  AND d.review_status = 0;
                """);

            migrationBuilder.AddColumn<string>(
                name: "document_file_name",
                table: "document_escalation_items",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "document_moderation_generation",
                table: "document_escalation_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "resolution_status",
                table: "document_escalation_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "admin_response",
                table: "document_escalation_items",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by_user_id",
                table: "document_escalation_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                table: "document_escalation_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE document_escalation_items AS item
                SET document_file_name = document.file_name,
                    document_moderation_generation = document.moderation_generation,
                    resolution_status = CASE
                        WHEN escalation.escalation_status = 'Pending' THEN 'Pending'
                        WHEN document.review_status = 1 THEN 'Approved'
                        ELSE 'Rejected'
                    END,
                    admin_response = escalation.admin_response,
                    resolved_by_user_id = escalation.resolved_by_user_id,
                    resolved_at = escalation.resolved_at
                FROM documents AS document,
                     document_escalations AS escalation
                WHERE item.document_id = document.id
                  AND item.escalation_id = escalation.id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "document_file_name",
                table: "document_escalation_items",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_document_escalation_items_documents_document_id",
                table: "document_escalation_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "document_id",
                table: "document_escalation_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_document_escalation_items_documents_document_id",
                table: "document_escalation_items",
                column: "document_id",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_document_escalation_items_users_resolved_by_user_id",
                table: "document_escalation_items",
                column: "resolved_by_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.DropIndex(
                name: "IX_document_escalation_items_document_id",
                table: "document_escalation_items");

            migrationBuilder.CreateIndex(
                name: "ix_document_escalation_items_status_generation",
                table: "document_escalation_items",
                columns: new[] { "resolution_status", "document_moderation_generation" });

            migrationBuilder.CreateIndex(
                name: "IX_document_escalation_items_resolved_by_user_id",
                table: "document_escalation_items",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_document_escalation_items_pending_document",
                table: "document_escalation_items",
                column: "document_id",
                unique: true,
                filter: "document_id IS NOT NULL AND resolution_status = 'Pending'");

            migrationBuilder.Sql("""
                UPDATE document_escalations
                SET escalation_status = 'Resolved'
                WHERE escalation_status IN ('Approved', 'Rejected');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_documents_moderation_generation_non_negative",
                table: "documents",
                sql: "moderation_generation >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_escalations_status",
                table: "document_escalations",
                sql: "escalation_status IN ('Pending', 'Resolved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_escalation_items_generation_non_negative",
                table: "document_escalation_items",
                sql: "document_moderation_generation >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_escalation_items_resolution_status",
                table: "document_escalation_items",
                sql: "resolution_status IN ('Pending', 'Approved', 'Rejected')");

            migrationBuilder.AddColumn<string>(
                name: "event_key",
                table: "user_notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "document_id",
                table: "user_notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE user_notifications
                SET event_key = 'folder-final:' || folder_id::text || ':' || submission_number::text;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "event_key",
                table: "user_notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_notifications_kind",
                table: "user_notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_notifications_outcome",
                table: "user_notifications");

            migrationBuilder.DropIndex(
                name: "ux_user_notifications_recipient_folder_submission",
                table: "user_notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_notifications_kind",
                table: "user_notifications",
                sql: "kind IN ('FolderModerationFinal', 'DocumentModerationFinal', 'EscalationResolved')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_notifications_outcome",
                table: "user_notifications",
                sql: "outcome IN ('Approved', 'Rejected', 'Mixed')");

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_document_id",
                table: "user_notifications",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_notifications_event_key",
                table: "user_notifications",
                column: "event_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_user_notifications_documents_document_id",
                table: "user_notifications",
                column: "document_id",
                principalTable: "documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE document_escalations AS escalation
                SET escalation_status = CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM document_escalation_items AS item
                        WHERE item.escalation_id = escalation.id
                          AND item.resolution_status = 'Rejected'
                    ) THEN 'Rejected'
                    ELSE 'Approved'
                END
                WHERE escalation.escalation_status = 'Resolved';
                """);

            migrationBuilder.DropForeignKey(name: "FK_user_notifications_documents_document_id", table: "user_notifications");
            migrationBuilder.DropIndex(name: "IX_user_notifications_document_id", table: "user_notifications");
            migrationBuilder.DropIndex(name: "IX_user_notifications_event_key", table: "user_notifications");
            migrationBuilder.DropCheckConstraint(name: "ck_user_notifications_kind", table: "user_notifications");
            migrationBuilder.DropCheckConstraint(name: "ck_user_notifications_outcome", table: "user_notifications");
            migrationBuilder.DropColumn(name: "document_id", table: "user_notifications");
            migrationBuilder.DropColumn(name: "event_key", table: "user_notifications");
            migrationBuilder.CreateIndex(
                name: "ux_user_notifications_recipient_folder_submission",
                table: "user_notifications",
                columns: new[] { "recipient_user_id", "folder_id", "submission_number" },
                unique: true);
            migrationBuilder.AddCheckConstraint(name: "ck_user_notifications_kind", table: "user_notifications", sql: "kind = 'FolderModerationFinal'");
            migrationBuilder.AddCheckConstraint(name: "ck_user_notifications_outcome", table: "user_notifications", sql: "outcome IN ('Approved', 'Rejected')");

            migrationBuilder.DropCheckConstraint(name: "ck_document_escalation_items_resolution_status", table: "document_escalation_items");
            migrationBuilder.DropCheckConstraint(name: "ck_document_escalation_items_generation_non_negative", table: "document_escalation_items");
            migrationBuilder.DropCheckConstraint(name: "ck_document_escalations_status", table: "document_escalations");
            migrationBuilder.DropCheckConstraint(name: "ck_documents_moderation_generation_non_negative", table: "documents");
            migrationBuilder.DropIndex(name: "ux_document_escalation_items_pending_document", table: "document_escalation_items");
            migrationBuilder.DropIndex(name: "ix_document_escalation_items_status_generation", table: "document_escalation_items");
            migrationBuilder.DropIndex(name: "IX_document_escalation_items_resolved_by_user_id", table: "document_escalation_items");
            migrationBuilder.DropForeignKey(name: "FK_document_escalation_items_users_resolved_by_user_id", table: "document_escalation_items");
            migrationBuilder.DropForeignKey(name: "FK_document_escalation_items_documents_document_id", table: "document_escalation_items");
            migrationBuilder.AlterColumn<Guid>(
                name: "document_id",
                table: "document_escalation_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
            migrationBuilder.AddForeignKey(
                name: "FK_document_escalation_items_documents_document_id",
                table: "document_escalation_items",
                column: "document_id",
                principalTable: "documents",
                principalColumn: "id");
            migrationBuilder.CreateIndex(name: "IX_document_escalation_items_document_id", table: "document_escalation_items", column: "document_id");
            migrationBuilder.DropColumn(name: "resolved_at", table: "document_escalation_items");
            migrationBuilder.DropColumn(name: "resolved_by_user_id", table: "document_escalation_items");
            migrationBuilder.DropColumn(name: "admin_response", table: "document_escalation_items");
            migrationBuilder.DropColumn(name: "resolution_status", table: "document_escalation_items");
            migrationBuilder.DropColumn(name: "document_moderation_generation", table: "document_escalation_items");
            migrationBuilder.DropColumn(name: "document_file_name", table: "document_escalation_items");
            migrationBuilder.DropColumn(name: "moderation_generation", table: "documents");
        }
    }
}
