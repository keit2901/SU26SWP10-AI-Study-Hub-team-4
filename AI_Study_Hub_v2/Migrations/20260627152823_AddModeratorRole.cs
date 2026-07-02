using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddModeratorRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "questions_json",
                table: "quizzes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AddColumn<string>(
                name: "scope_json",
                table: "quizzes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.CreateTable(
                name: "ai_answer_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    details = table.Column<string>(type: "text", nullable: true),
                    context_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    sources_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "open"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_answer_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_answer_reports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quiz_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    score = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quiz_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_quiz_attempts_quizzes_quiz_id",
                        column: x => x.quiz_id,
                        principalTable: "quizzes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_quiz_attempts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "created_at", "description", "role_name" },
                values: new object[] { 3, new DateTimeOffset(new DateTime(2026, 6, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Kiểm duyệt viên cộng đồng, có quyền xem và xử lý báo cáo vi phạm nhưng không thể thay đổi cấu hình hệ thống hoặc quản lý người dùng", "Moderator" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_answer_reports_created_at",
                table: "ai_answer_reports",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_ai_answer_reports_status",
                table: "ai_answer_reports",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_ai_answer_reports_user_id",
                table: "ai_answer_reports",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_attempts_created_at",
                table: "quiz_attempts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_attempts_quiz_id",
                table: "quiz_attempts",
                column: "quiz_id");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_attempts_user_id",
                table: "quiz_attempts",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_quizzes_users_user_id",
                table: "quizzes",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_quizzes_users_user_id",
                table: "quizzes");

            migrationBuilder.DropTable(
                name: "ai_answer_reports");

            migrationBuilder.DropTable(
                name: "quiz_attempts");

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: 3);

            migrationBuilder.DropColumn(
                name: "scope_json",
                table: "quizzes");

            migrationBuilder.AlterColumn<string>(
                name: "questions_json",
                table: "quizzes",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");
        }
    }
}
