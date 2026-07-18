using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AiAnswerReportServiceTests
{
    [Test]
    public async Task ReportAsync_PersistsOpenReport_ForActiveUser()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var sut = new AiAnswerReportService(db);
        var source = new AiChatSourceDto("S1", Guid.NewGuid(), "rag.pdf", 0, 1, "source excerpt", 0.12);

        var response = await sut.ReportAsync(user.SupabaseUserId, new AiAnswerReportRequest(
            " What is RAG? ",
            " Bad answer ",
            "incorrect_fact",
            "Needs correction",
            new { folderId = Guid.NewGuid() },
            new[] { source }));

        response.Id.Should().NotBeEmpty();
        response.Status.Should().Be("open");
        response.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var row = await db.AiAnswerReports.AsNoTracking().SingleAsync();
        row.UserId.Should().Be(user.Id);
        row.Question.Should().Be("What is RAG?");
        row.Answer.Should().Be("Bad answer");
        row.Reason.Should().Be("incorrect_fact");
        row.Status.Should().Be("open");
        row.SourcesJson.Should().Contain("rag.pdf");
        JsonDocument.Parse(row.ContextJson).RootElement.TryGetProperty("folderId", out _).Should().BeTrue();
    }

    [Test]
    public async Task ReportAsync_MissingQuestion_Throws400_AndDoesNotPersist()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db);
        var sut = new AiAnswerReportService(db);

        var act = () => sut.ReportAsync(user.SupabaseUserId, new AiAnswerReportRequest(" ", "answer", "reason"));

        var ex = await act.Should().ThrowAsync<AiStudyFeatureException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Code.Should().Be("question_required");
        db.AiAnswerReports.Should().BeEmpty();
    }

    [Test]
    public async Task ReportAsync_InactiveUser_Throws403()
    {
        using var db = TestDb.CreateInMemory();
        var user = SeedActiveStudent(db, isActive: false);
        var sut = new AiAnswerReportService(db);

        var act = () => sut.ReportAsync(user.SupabaseUserId, new AiAnswerReportRequest("question", "answer", "reason"));

        var ex = await act.Should().ThrowAsync<AiStudyFeatureException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.Code.Should().Be("user_inactive");
    }

    private static User SeedActiveStudent(AppDbContext db, bool isActive = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..10],
            FullName = "Test User",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }
}
