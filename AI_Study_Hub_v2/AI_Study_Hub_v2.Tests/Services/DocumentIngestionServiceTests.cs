using System.Text;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class DocumentIngestionServiceTests
{
    [Test]
    public async Task IngestAsync_OwnedDocument_StoresChunksAndMarksReady()
    {
        await using var db = CreateDb();
        var profile = SeedActiveStudent(db);
        var document = SeedDocument(db, profile.Id);
        var extraction = new FakeTextExtractionService(new[]
        {
            new ExtractedPage(1, "First page text."),
            new ExtractedPage(2, "Second page text."),
        });
        var sut = BuildSut(db, extraction);

        var result = await sut.IngestAsync(document.Id, profile.SupabaseUserId);

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().Be(1);
        result.ErrorMessage.Should().BeNull();

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        reloaded.Status.Should().Be(DocumentStatus.Ready);
        reloaded.PageCount.Should().Be(2);
        reloaded.ErrorMessage.Should().BeNull();

        var chunks = await db.DocumentChunks
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();
        chunks.Select(c => c.ChunkIndex).Should().Equal(0);
        chunks.Select(c => c.PageNumber).Should().Equal(1);
        chunks.Select(c => c.Content).Should().Equal("First page text. Second page text.");
        chunks.Should().OnlyContain(c => c.TokenCount > 0);
        chunks[0].TokenCount.Should().Be(new ConservativeTokenEstimator().Estimate(chunks[0].Content));
    }

    [Test]
    public async Task IngestAsync_Reingest_ReplacesExistingChunks()
    {
        await using var db = CreateDb();
        var profile = SeedActiveStudent(db);
        var document = SeedDocument(db, profile.Id);
        db.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = 0,
            PageNumber = 7,
            Content = "Old content",
            TokenCount = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var extraction = new FakeTextExtractionService(new[] { new ExtractedPage(3, "New content") });
        var sut = BuildSut(db, extraction);

        var result = await sut.IngestAsync(document.Id, profile.SupabaseUserId);

        result.Success.Should().BeTrue();
        result.ChunkCount.Should().Be(1);

        var chunks = await db.DocumentChunks.Where(c => c.DocumentId == document.Id).ToListAsync();
        chunks.Should().ContainSingle();
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].PageNumber.Should().Be(3);
        chunks[0].Content.Should().Be("New content");
    }

    [Test]
    public async Task IngestAsync_NoExtractableText_MarksFailedAndReturnsFailure()
    {
        await using var db = CreateDb();
        var profile = SeedActiveStudent(db);
        var document = SeedDocument(db, profile.Id);
        var extraction = new FakeTextExtractionService(new[]
        {
            new ExtractedPage(1, "  "),
            new ExtractedPage(2, "\r\n"),
        });
        var sut = BuildSut(db, extraction);

        var result = await sut.IngestAsync(document.Id, profile.SupabaseUserId);

        result.Success.Should().BeFalse();
        result.ChunkCount.Should().Be(0);
        result.ErrorMessage.Should().Contain("No extractable text");

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        reloaded.Status.Should().Be(DocumentStatus.Failed);
        reloaded.ErrorMessage.Should().Contain("No extractable text");
        (await db.DocumentChunks.CountAsync(c => c.DocumentId == document.Id)).Should().Be(0);
    }

    [Test]
    public async Task IngestAsync_ExtractionFailure_MarksFailedAndReturnsFailure()
    {
        await using var db = CreateDb();
        var profile = SeedActiveStudent(db);
        var document = SeedDocument(db, profile.Id);
        var sut = BuildSut(db, new ThrowingTextExtractionService("PDF parser failed"));

        var result = await sut.IngestAsync(document.Id, profile.SupabaseUserId);

        result.Success.Should().BeFalse();
        result.ChunkCount.Should().Be(0);
        result.ErrorMessage.Should().Contain("PDF parser failed");

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        reloaded.Status.Should().Be(DocumentStatus.Failed);
        reloaded.ErrorMessage.Should().Contain("PDF parser failed");
        (await db.DocumentChunks.CountAsync(c => c.DocumentId == document.Id)).Should().Be(0);
    }

    [Test]
    public async Task IngestAsync_NotOwner_ReturnsFailureAndDoesNotModifyDocument()
    {
        await using var db = CreateDb();
        var caller = SeedActiveStudent(db);
        var owner = SeedActiveStudent(db);
        var document = SeedDocument(db, owner.Id);
        var storage = new FakeStorageReadService();
        var sut = BuildSut(db, new FakeTextExtractionService(Array.Empty<ExtractedPage>()), storage);

        var result = await sut.IngestAsync(document.Id, caller.SupabaseUserId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not exist or does not belong");
        storage.OpenCount.Should().Be(0);

        var reloaded = await db.Documents.SingleAsync(d => d.Id == document.Id);
        reloaded.Status.Should().Be(DocumentStatus.Ready);
        reloaded.ErrorMessage.Should().BeNull();
    }

    private static DocumentIngestionService BuildSut(
        AppDbContext db,
        ITextExtractionService extraction,
        IDocumentStorageReadService? storage = null,
        IEmbeddingService? embedding = null,
        RagOptions? options = null,
        IImageDescriptionService? imageDescription = null,
        GroqOptions? groqOptions = null)
    {
        var ragOptions = options ?? new RagOptions
        {
            ChunkSizeChars = 1000,
            ChunkOverlapChars = 200,
            EmbeddingDimensions = DocumentChunk.EmbeddingDimension,
        };

        return new DocumentIngestionService(
            db,
            storage ?? new FakeStorageReadService(),
            extraction,
            BuildChunkingService(ragOptions),
            new ConservativeTokenEstimator(),
            embedding ?? new FakeEmbeddingService(ragOptions.EmbeddingDimensions),
            imageDescription ?? new FakeImageDescriptionService(),
            Microsoft.Extensions.Options.Options.Create(ragOptions),
            Microsoft.Extensions.Options.Options.Create(new OllamaOptions
            {
                Model = "all-minilm:l6-v2"
            }),
            Microsoft.Extensions.Options.Options.Create(groqOptions ?? new GroqOptions()),
            NullLogger<DocumentIngestionService>.Instance);

    }

    private static ChunkingService BuildChunkingService(RagOptions options) =>
        new(
            new BlockParser(),
            new SentenceSplitter(),
            new ChunkMerger(Microsoft.Extensions.Options.Options.Create(options)));

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new IngestionTestDbContext(options);
        db.Roles.Add(new Role
        {
            Id = 2,
            RoleName = Role.StudentRoleName,
            Description = "Student",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        return db;
    }

    private static User SeedActiveStudent(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..10],
            FullName = "Test User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Document SeedDocument(AppDbContext db, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FileName = "notes.pdf",
            StoragePath = $"users/{userId:N}/2026/{Guid.NewGuid():N}-notes.pdf",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Documents.Add(document);
        db.SaveChanges();
        return document;
    }

    private static float[] CreateEmbedding(int dimensions = DocumentChunk.EmbeddingDimension) =>
        Enumerable.Range(0, dimensions).Select(i => i / 1000f).ToArray();

    private sealed class IngestionTestDbContext : AppDbContext
    {
        public IngestionTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Ignore(c => c.Embedding);
        }
    }

    private sealed class FakeStorageReadService : IDocumentStorageReadService
    {
        public int OpenCount { get; private set; }

        public Task<Stream> OpenReadAsync(Document document, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("fake file bytes")));
        }
    }

    private sealed class FakeTextExtractionService : ITextExtractionService
    {
        private readonly IReadOnlyList<ExtractedPage> _pages;

        public FakeTextExtractionService(IReadOnlyList<ExtractedPage> pages)
        {
            _pages = pages;
        }

        public Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(
            Stream fileStream,
            string mimeType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_pages);
    }

    private sealed class ThrowingTextExtractionService : ITextExtractionService
    {
        private readonly string _message;

        public ThrowingTextExtractionService(string message)
        {
            _message = message;
        }

        public Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(
            Stream fileStream,
            string mimeType,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(_message);
    }

    private sealed class FakeImageDescriptionService : IImageDescriptionService
    {
        public Task<string> DescribeAsync(IReadOnlyList<ExtractedImage> pageImages, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        private readonly int _dimensions;

        public FakeEmbeddingService(int dimensions)
        {
            _dimensions = dimensions;
        }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateEmbedding(_dimensions));
    }
}
