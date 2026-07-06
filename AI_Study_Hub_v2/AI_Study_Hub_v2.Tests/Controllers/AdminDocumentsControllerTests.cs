using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class AdminDocumentsControllerTests
{
    [Test]
    public async Task List_HappyPath_ReturnsDocuments()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice@test.com", "Alice");
        SeedDocument(db, user.Id, "SWP391", DocumentStatus.Ready, "notes.pdf");

        var sut = BuildSut(db);

        var result = await sut.List(page: 1, size: 20);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var docs = ok.Value.Should().BeAssignableTo<IReadOnlyList<Dtos.AdminDocumentDto>>().Subject;
        docs.Should().HaveCount(1);
        docs[0].FileName.Should().Be("notes.pdf");
        docs[0].OwnerName.Should().Be("Alice");
    }

    [Test]
    public async Task List_FilterByStatus_ReturnsOnlyMatching()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice@test.com", "Alice");
        SeedDocument(db, user.Id, "SWP391", DocumentStatus.Ready, "ready.pdf");
        SeedDocument(db, user.Id, "PRN212", DocumentStatus.Failed, "failed.pdf");

        var sut = BuildSut(db);

        var result = await sut.List(status: "Ready", page: 1, size: 20);
        var docs = ((OkObjectResult)result.Result!).Value.Should().BeAssignableTo<IReadOnlyList<Dtos.AdminDocumentDto>>().Subject;
        docs.Should().ContainSingle().Which.SubjectCode.Should().Be("SWP391");
    }

    [Test]
    public async Task List_Pagination_ReturnsCorrectPage()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice@test.com", "Alice");
        for (var i = 0; i < 5; i++)
            SeedDocument(db, user.Id, $"SUB{i:00}", DocumentStatus.Ready, $"doc{i}.pdf");

        var sut = BuildSut(db);

        var page1 = ((OkObjectResult)(await sut.List(page: 1, size: 2)).Result!).Value
            .Should().BeAssignableTo<IReadOnlyList<Dtos.AdminDocumentDto>>().Subject;
        var page2 = ((OkObjectResult)(await sut.List(page: 2, size: 2)).Result!).Value
            .Should().BeAssignableTo<IReadOnlyList<Dtos.AdminDocumentDto>>().Subject;

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
    }

    [Test]
    public async Task GetById_Exists_ReturnsDetailWithChunks()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, "alice@test.com", "Alice");
        var doc = SeedDocument(db, user.Id, "SWP391", DocumentStatus.Ready, "notes.pdf");
        SeedChunk(db, doc.Id, 0, "Content chunk 0", 1);
        SeedChunk(db, doc.Id, 1, "Content chunk 1", 2);

        var sut = BuildSut(db);

        var result = await sut.GetById(doc.Id);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = ok.Value.Should().BeAssignableTo<Dtos.AdminDocumentDetailDto>().Subject;
        detail.FileName.Should().Be("notes.pdf");
        detail.Chunks.Should().HaveCount(2);
        detail.Chunks[0].ChunkIndex.Should().Be(0);
    }

    [Test]
    public async Task GetById_NotFound_Returns404()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = BuildSut(db);

        var result = await sut.GetById(Guid.NewGuid());

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);
    }

    private static AdminDocumentsController BuildSut(AppDbContext db)
    {
        var ragOptions = Options.Create(new AI_Study_Hub_v2.Options.RagOptions { ChunkingStrategy = "fixed" });
        var ingestionMock = new Mock<IDocumentIngestionService>();
        return new AdminDocumentsController(db, ingestionMock.Object, ragOptions, NullLogger<AdminDocumentsController>.Instance);
    }

    private static User SeedUser(AppDbContext db, string email, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Email = email,
            Username = $"u{Guid.NewGuid():N}"[..12],
            FullName = fullName,
            IsActive = true,
            DailyTokenQuota = 25_000,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Document SeedDocument(AppDbContext db, Guid userId, string subjectCode, DocumentStatus status, string fileName)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = fileName,
            SubjectCode = subjectCode,
            Status = status,
            MimeType = "application/pdf",
            FileSizeBytes = 1024,
            StoragePath = $"docs/{fileName}",
            Semester = "SU26",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
    }

    private static DocumentChunk SeedChunk(AppDbContext db, Guid documentId, int chunkIndex, string content, int? pageNumber)
    {
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = content,
            PageNumber = pageNumber,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.DocumentChunks.Add(chunk);
        db.SaveChanges();
        return chunk;
    }
}
