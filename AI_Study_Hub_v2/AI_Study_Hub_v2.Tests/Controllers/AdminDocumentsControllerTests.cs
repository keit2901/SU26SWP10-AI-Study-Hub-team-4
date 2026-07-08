using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RagOptions = AI_Study_Hub_v2.Options.RagOptions;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class AdminDocumentsControllerTests
{
    [Test]
    public async Task List_HappyPath_ReturnsDocuments()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice", "Alice");
        SeedDocument(db, user.Id, "SWP391", DocumentStatus.Ready, "notes.pdf");

        var sut = BuildSut(db);
        var result = await sut.List(null, null, null, 1, 20, CancellationToken.None);
        var docs = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IReadOnlyList<AdminDocumentDto>>().Subject;
        docs.Should().HaveCount(1);
        docs[0].FileName.Should().Be("notes.pdf");
    }

    [Test]
    public async Task List_FilterByStatus_ReturnsOnlyMatching()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice", "Alice");
        SeedDocument(db, user.Id, "SWP391", DocumentStatus.Ready, "ready.pdf");
        SeedDocument(db, user.Id, "PRN212", DocumentStatus.Failed, "failed.pdf");

        var sut = BuildSut(db);
        var result = await sut.List("Ready", null, null, 1, 20, CancellationToken.None);
        var docs = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IReadOnlyList<AdminDocumentDto>>().Subject;
        docs.Should().ContainSingle().Which.SubjectCode.Should().Be("SWP391");
    }

    [Test]
    public async Task List_Pagination_ReturnsCorrectPage()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice", "Alice");
        for (var i = 0; i < 5; i++)
            SeedDocument(db, user.Id, $"SUB{i:00}", DocumentStatus.Ready, $"doc{i}.pdf");

        var sut = BuildSut(db);
        var page1 = ((OkObjectResult)(await sut.List(null, null, null, 1, 2, CancellationToken.None)).Result!).Value
            .Should().BeAssignableTo<IReadOnlyList<AdminDocumentDto>>().Subject;
        var page2 = ((OkObjectResult)(await sut.List(null, null, null, 2, 2, CancellationToken.None)).Result!).Value
            .Should().BeAssignableTo<IReadOnlyList<AdminDocumentDto>>().Subject;
        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
    }

    [Test]
    [Ignore("DocumentChunk not supported in InMemory provider")]
    public async Task GetById_Exists_ReturnsDetail()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice", "Alice");
        var doc = SeedDocument(db, user.Id, "SWP391", DocumentStatus.Ready, "notes.pdf");

        var sut = BuildSut(db);
        var result = await sut.GetById(doc.Id, CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    [Test]
    [Ignore("DocumentChunk not supported in InMemory provider")]
    public async Task GetById_NotFound_Returns404()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = BuildSut(db);
        var result = await sut.GetById(Guid.NewGuid(), CancellationToken.None);
        ((NotFoundObjectResult)result.Result!).StatusCode.Should().Be(404);
    }

    private static AdminDocumentsController BuildSut(AppDbContext db)
    {
        var ragOptions = Microsoft.Extensions.Options.Options.Create(new RagOptions { ChunkingStrategy = "fixed" });
        var mock = new Mock<IDocumentIngestionService>();
        return new AdminDocumentsController(db, mock.Object, ragOptions, NullLogger<AdminDocumentsController>.Instance);
    }

    private static User SeedUser(AppDbContext db, string username, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), RoleId = 2, SupabaseUserId = Guid.NewGuid(),
            Username = username, FullName = fullName, IsActive = true, DailyTokenQuota = 25000,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Document SeedDocument(AppDbContext db, Guid userId, string subjectCode, DocumentStatus status, string fileName)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), UserId = userId, FileName = fileName, SubjectCode = subjectCode,
            Status = status, MimeType = "application/pdf", FileSizeBytes = 1024,
            StoragePath = $"docs/{fileName}", Semester = "SU26",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
    }
}
