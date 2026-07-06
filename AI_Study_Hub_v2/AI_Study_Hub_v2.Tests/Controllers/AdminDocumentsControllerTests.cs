using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class AdminDocumentsControllerTests
{
    [Test]
    public async Task ReingestAll_IncludesFailedDocuments_AndReturnsCurrentStrategy()
    {
        await using var db = CreateDb();
        var owner = SeedUser(db);
        var ready = SeedDocument(db, owner.Id, "ready.pdf", DocumentStatus.Ready);
        var failed = SeedDocument(db, owner.Id, "failed.pdf", DocumentStatus.Failed);
        SeedDocument(db, owner.Id, "processing.pdf", DocumentStatus.Processing);
        await db.SaveChangesAsync();

        var ingestion = new Mock<IDocumentIngestionService>();
        ingestion.Setup(service => service.IngestAsync(ready.Id, owner.SupabaseUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult(ready.Id, 2, true, null));
        ingestion.Setup(service => service.IngestAsync(failed.Id, owner.SupabaseUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult(failed.Id, 1, true, null));

        var sut = new AdminDocumentsController(
            db,
            ingestion.Object,
            Microsoft.Extensions.Options.Options.Create(new RagOptions { ChunkingStrategy = "semantic" }),
            NullLogger<AdminDocumentsController>.Instance);

        var result = await sut.ReingestAll(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<ReingestAllDocumentsResponse>().Subject;
        payload.Total.Should().Be(2);
        payload.Succeeded.Should().Be(2);
        payload.Failed.Should().Be(0);
        payload.ChunkingStrategy.Should().Be("semantic");
    }

    private static AppDbContext CreateDb()
        => TestDb.CreateInMemoryWithDocuments();

    private static User SeedUser(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 1,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"admin-{Guid.NewGuid():N}"[..12],
            FullName = "Admin User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Document SeedDocument(AppDbContext db, Guid userId, string fileName, DocumentStatus status)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = fileName,
            StoragePath = $"users/{userId:N}/{fileName}",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Documents.Add(document);
        return document;
    }
}
