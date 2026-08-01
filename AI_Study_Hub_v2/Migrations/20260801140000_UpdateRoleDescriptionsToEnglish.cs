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
                keyColumn: "id",
                keyValue: 1,
                column: "description",
                value: "System administrator responsible for managing users, moderating documents, and configuring AI settings.");

            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "id",
                keyValue: 2,
                column: "description",
                value: "Student who uses personal learning resources, participates in RAG conversations, and completes review quizzes.");

            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "id",
                keyValue: 3,
                column: "description",
                value: "Community moderator who reviews and handles violation reports without access to system settings or user management.");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "id",
                keyValue: 1,
                column: "description",
                value: "Quản trị viên hệ thống, có quyền điều phối nhân sự, kiểm duyệt tài liệu và thay đổi tham số cấu hình AI");

            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "id",
                keyValue: 2,
                column: "description",
                value: "Sinh viên khai thác tài nguyên học tập cá nhân, thực hiện hội thoại RAG và tham gia kiểm tra ôn tập");

            migrationBuilder.UpdateData(
                table: "roles",
                keyColumn: "id",
                keyValue: 3,
                column: "description",
                value: "Kiểm duyệt viên cộng đồng, có quyền xem và xử lý báo cáo vi phạm nhưng không thể thay đổi cấu hình hệ thống hoặc quản lý người dùng");
        }
    }
}
