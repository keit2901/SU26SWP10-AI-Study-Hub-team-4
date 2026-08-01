using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801140000_UpdateRoleDescriptionsToEnglish")]
    public partial class UpdateRoleDescriptionsToEnglish : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "roles",
                schema: "public",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "integer" },
                keyValues: new object[] { 1 },
                columns: new[] { "description" },
                columnTypes: new[] { "text" },
                values: new object[] { "System administrator responsible for managing users, moderating documents, and configuring AI settings." });

            migrationBuilder.UpdateData(
                table: "roles",
                schema: "public",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "integer" },
                keyValues: new object[] { 2 },
                columns: new[] { "description" },
                columnTypes: new[] { "text" },
                values: new object[] { "Student who uses personal learning resources, participates in RAG conversations, and completes review quizzes." });

            migrationBuilder.UpdateData(
                table: "roles",
                schema: "public",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "integer" },
                keyValues: new object[] { 3 },
                columns: new[] { "description" },
                columnTypes: new[] { "text" },
                values: new object[] { "Community moderator who reviews and handles violation reports without access to system settings or user management." });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "roles",
                schema: "public",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "integer" },
                keyValues: new object[] { 1 },
                columns: new[] { "description" },
                columnTypes: new[] { "text" },
                values: new object[] { "Quản trị viên hệ thống, có quyền điều phối nhân sự, kiểm duyệt tài liệu và thay đổi tham số cấu hình AI" });

            migrationBuilder.UpdateData(
                table: "roles",
                schema: "public",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "integer" },
                keyValues: new object[] { 2 },
                columns: new[] { "description" },
                columnTypes: new[] { "text" },
                values: new object[] { "Sinh viên khai thác tài nguyên học tập cá nhân, thực hiện hội thoại RAG và tham gia kiểm tra ôn tập" });

            migrationBuilder.UpdateData(
                table: "roles",
                schema: "public",
                keyColumns: new[] { "id" },
                keyColumnTypes: new[] { "integer" },
                keyValues: new object[] { 3 },
                columns: new[] { "description" },
                columnTypes: new[] { "text" },
                values: new object[] { "Kiểm duyệt viên cộng đồng, có quyền xem và xử lý báo cáo vi phạm nhưng không thể thay đổi cấu hình hệ thống hoặc quản lý người dùng" });
        }
    }
}
