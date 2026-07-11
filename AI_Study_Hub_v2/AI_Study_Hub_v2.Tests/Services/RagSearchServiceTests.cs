using System.Globalization;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Pgvector;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class RagSearchServiceTests
{
    [Test]
    public async Task SearchAsync_FiltersChunksByCurrentEmbeddingModel()
    {
        using var db = CreateDb();
        var user = SeedUser(db);

        var currentDocument = new Document
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FileName = "current.pdf",
            StoragePath = "documents/current.pdf",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var oldDocument = new Document
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FileName = "old.pdf",
            StoragePath = "documents/old.pdf",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Documents.AddRange(currentDocument, oldDocument);

        db.DocumentChunks.AddRange(
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = currentDocument.Id,
                ChunkIndex = 0,
                Content = "current model chunk",
                Embedding = new Vector(TestEmbedding(1f)),
                EmbeddingModel = "all-minilm:l6-v2",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = oldDocument.Id,
                ChunkIndex = 0,
                Content = "old model chunk",
                Embedding = new Vector(TestEmbedding(1f)),
                EmbeddingModel = "old-model",
                CreatedAt = DateTimeOffset.UtcNow
            });

        await db.SaveChangesAsync();

        var sut = BuildSut(db, TestEmbedding(1f), currentModel: "all-minilm:l6-v2");

        var results = await sut.SearchAsync(
            user.SupabaseUserId,
            new RagSearchRequest("test", null, null, null, null, 5, null, null),
            CancellationToken.None);

        results.Should().ContainSingle();
        results.Single().DocumentId.Should().Be(currentDocument.Id);
    }

    [Test]
    public async Task SearchAsync_IgnoresForeignUserChunks()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var other = SeedUser(db);
        var ownerDoc = SeedDocument(db, owner.Id, fileName: "owner.pdf");
        var foreignDoc = SeedDocument(db, other.Id, fileName: "foreign.pdf");
        SeedChunk(db, ownerDoc, chunkIndex: 0, content: "owned vector content", embedding: UnitAt(0));
        SeedChunk(db, foreignDoc, chunkIndex: 0, content: "foreign vector content", embedding: UnitAt(0));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest("vector", null, null, null, null, TopK: 5));

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(ownerDoc.Id);
        results.Should().NotContain(r => r.DocumentId == foreignDoc.Id);
    }

    [Test]
    public async Task SearchAsync_RespectsDocumentFilter()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var target = SeedDocument(db, owner.Id, fileName: "target.pdf");
        var ignored = SeedDocument(db, owner.Id, fileName: "ignored.pdf");
        SeedChunk(db, target, chunkIndex: 0, content: "target content", embedding: UnitAt(0));
        SeedChunk(db, ignored, chunkIndex: 0, content: "ignored content", embedding: UnitAt(0));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest("content", target.Id, null, null, null, TopK: 5));

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(target.Id);
    }

    [Test]
    public async Task SearchAsync_RespectsFolderSubjectAndSemesterFilters()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var folderId = Guid.NewGuid();
        var otherFolderId = Guid.NewGuid();
        db.Folders.Add(new Folder
        {
            Id = folderId,
            UserId = owner.Id,
            Name = "RAG",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Folders.Add(new Folder
        {
            Id = otherFolderId,
            UserId = owner.Id,
            Name = "Other",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var matching = SeedDocument(db, owner.Id, folderId: folderId, subjectCode: "SWP391", semester: "SU26", fileName: "match.pdf");
        var wrongFolder = SeedDocument(db, owner.Id, folderId: otherFolderId, subjectCode: "SWP391", semester: "SU26", fileName: "wrong-folder.pdf");
        var wrongSubject = SeedDocument(db, owner.Id, folderId: folderId, subjectCode: "PRN232", semester: "SU26", fileName: "wrong-subject.pdf");
        var wrongSemester = SeedDocument(db, owner.Id, folderId: folderId, subjectCode: "SWP391", semester: "FA25", fileName: "wrong-semester.pdf");

        foreach (var doc in new[] { matching, wrongFolder, wrongSubject, wrongSemester })
        {
            SeedChunk(db, doc, chunkIndex: 0, content: doc.FileName, embedding: UnitAt(0));
        }
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(owner.SupabaseUserId,
            new RagSearchRequest("filtered", null, folderId, "swp391", "su26", TopK: 10));

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(matching.Id);
    }

    [Test]
    public async Task SearchAsync_DocumentIds_AreScopedToCurrentFolder()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var folderA = Guid.NewGuid();
        var folderB = Guid.NewGuid();
        db.Folders.Add(new Folder { Id = folderA, UserId = owner.Id, Name = "A", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.Folders.Add(new Folder { Id = folderB, UserId = owner.Id, Name = "B", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });

        var allowed = SeedDocument(db, owner.Id, folderId: folderB, fileName: "allowed.pdf");
        var blocked = SeedDocument(db, owner.Id, folderId: folderA, fileName: "blocked.pdf");
        SeedChunk(db, allowed, chunkIndex: 0, content: "allowed folder content", embedding: UnitAt(0));
        SeedChunk(db, blocked, chunkIndex: 0, content: "blocked folder content", embedding: UnitAt(0));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(owner.SupabaseUserId,
            new RagSearchRequest("content", null, folderB, null, null, TopK: 10, DocumentIds: new[] { allowed.Id, blocked.Id }));

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(allowed.Id);
    }

    [Test]
    public async Task SearchAsync_CapsTopK_FromOptions()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        for (var i = 0; i < 5; i++)
        {
            var doc = SeedDocument(db, owner.Id, fileName: $"doc-{i}.pdf");
            SeedChunk(db, doc, chunkIndex: 0, content: $"content {i}", embedding: UnitAt(0));
        }
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0), new RagOptions { MaxTopK = 2, DefaultTopK = 2 });

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest("content", null, null, null, null, TopK: 50));

        results.Should().HaveCount(2);
    }

    [Test]
    public async Task SearchAsync_ReturnsSourceMetadataAndScore()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var doc = SeedDocument(db, owner.Id, fileName: "lecture.pdf");
        SeedChunk(db, doc, chunkIndex: 7, pageNumber: 3, content: "  spaced   excerpt   content  ", embedding: UnitAt(0));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest("excerpt", null, null, null, null, TopK: 5));

        var result = results.Should().ContainSingle().Subject;
        result.SourceLabel.Should().Be("lecture.pdf (chunk 7, p. 3)");
        result.DocumentId.Should().Be(doc.Id);
        result.FileName.Should().Be("lecture.pdf");
        result.ChunkIndex.Should().Be(7);
        result.PageNumber.Should().Be(3);
        result.ContentExcerpt.Should().Be("spaced excerpt content");
        result.Score.Should().BeApproximately(1.06d, 0.0001d);
    }

    [Test]
    public async Task SearchAsync_KeywordMode_PrefersExactKeywordMatch()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var denseDoc = SeedDocument(db, owner.Id, fileName: "dense.pdf");
        var keywordDoc = SeedDocument(db, owner.Id, fileName: "keyword.pdf");
        SeedChunk(db, denseDoc, 0, "general architecture overview", UnitAt(0));
        SeedChunk(db, keywordDoc, 0, "SWP391 capstone plan and checklist", UnitAt(1));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0), new RagOptions { SearchMode = "keyword", ReRankEnabled = false });

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest(
            "SWP391 plan",
            DocumentId: null,
            FolderId: null,
            SubjectCode: null,
            Semester: null,
            TopK: 5,
            DocumentIds: null,
            TopicKeyword: null,
            SearchMode: "keyword"));

        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be(keywordDoc.Id);
    }

    [Test]
    public async Task SearchAsync_HybridMode_UsesVectorAndKeywordFusion()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var denseDoc = SeedDocument(db, owner.Id, fileName: "dense.pdf");
        var hybridDoc = SeedDocument(db, owner.Id, fileName: "hybrid.pdf");
        SeedChunk(db, denseDoc, 0, "general architecture overview", UnitAt(0));
        SeedChunk(db, hybridDoc, 0, "SWP391 capstone plan and checklist", UnitAt(1));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0), new RagOptions
        {
            SearchMode = "hybrid",
            HybridSearchEnabled = true,
            VectorWeight = 0.2d,
            ReRankEnabled = false
        });

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest(
            "SWP391 plan",
            DocumentId: null,
            FolderId: null,
            SubjectCode: null,
            Semester: null,
            TopK: 5,
            DocumentIds: null,
            TopicKeyword: null,
            SearchMode: "hybrid"));

        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be(hybridDoc.Id);
    }

    [Test]
    public async Task SearchAsync_ReRankEnabled_ReordersTopCandidates()
    {
        using var db = CreateDb();
        var owner = SeedUser(db);
        var firstDoc = SeedDocument(db, owner.Id, fileName: "first.pdf");
        var secondDoc = SeedDocument(db, owner.Id, fileName: "second.pdf");
        SeedChunk(db, firstDoc, 0, "first chunk", UnitAt(0));
        SeedChunk(db, secondDoc, 0, "second chunk", UnitAt(1));
        await db.SaveChangesAsync();

        var reRank = new Mock<IReRankService>();
        reRank.Setup(x => x.ReRankAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ReRankCandidate>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<ReRankCandidate> candidates, int topN, CancellationToken _) =>
                candidates
                    .Select(candidate => candidate.DocumentId == secondDoc.Id
                        ? candidate with { ReRankScore = 0.95d }
                        : candidate with { ReRankScore = 0.10d })
                    .OrderByDescending(candidate => candidate.ReRankScore)
                    .Take(topN)
                    .ToList());

        var sut = BuildSut(db, UnitAt(0), new RagOptions
        {
            ReRankEnabled = true,
            ReRankCandidateCount = 5,
            ReRankTopN = 5
        }, reRankService: reRank.Object);

        var results = await sut.SearchAsync(owner.SupabaseUserId, new RagSearchRequest(
            "chunk",
            DocumentId: null,
            FolderId: null,
            SubjectCode: null,
            Semester: null,
            TopK: 5,
            DocumentIds: null,
            TopicKeyword: null,
            SearchMode: null));

        results.Should().HaveCount(2);
        results[0].DocumentId.Should().Be(secondDoc.Id);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new RagTestDbContext(options);
    }

    private sealed class RagTestDbContext : AppDbContext
    {
        public RagTestDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DocumentChunk>()
                .Property(c => c.Embedding)
                .HasConversion(
                    vector => string.Join(',', vector.ToArray().Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
                    value => new Vector(value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(item => float.Parse(item, CultureInfo.InvariantCulture)).ToArray()));
        }
    }

    private static RagSearchService BuildSut(
        AppDbContext db,
        float[] queryEmbedding,
        RagOptions? options = null,
        string currentModel = "all-minilm:l6-v2",
        IReRankService? reRankService = null)
    {
        var embeddings = new Mock<IEmbeddingService>();
        embeddings
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryEmbedding);

        reRankService ??= CreatePassThroughReRankService();

        return new RagSearchService(
            db,
            embeddings.Object,
            reRankService,
            OptionsFactory.Create(options ?? new RagOptions()),
            OptionsFactory.Create(new OllamaOptions
            {
                Model = currentModel
            }),
            NullLogger<RagSearchService>.Instance);
    }

    private static IReRankService CreatePassThroughReRankService()
    {
        var reRank = new Mock<IReRankService>();
        reRank.Setup(x => x.ReRankAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<ReRankCandidate>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<ReRankCandidate> candidates, int topN, CancellationToken _) =>
                candidates
                    .Take(topN)
                    .Select(candidate => candidate with { ReRankScore = candidate.InitialScore })
                    .ToList());

        return reRank.Object;
    }

    private static User SeedUser(AppDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..10],
            FullName = "RAG Test User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Document SeedDocument(
        AppDbContext db,
        Guid userId,
        string fileName,
        Guid? folderId = null,
        string subjectCode = "SWP391",
        string semester = "SU26")
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = fileName,
            StoragePath = $"users/{userId:N}/2026/{Guid.NewGuid():N}-{fileName}",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = subjectCode,
            Semester = semester,
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.Documents.Add(doc);
        return doc;
    }

    private static void SeedChunk(
        AppDbContext db,
        Document document,
        int chunkIndex,
        string content,
        float[] embedding,
        int? pageNumber = null)
    {
        db.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            Document = document,
            ChunkIndex = chunkIndex,
            PageNumber = pageNumber,
            Content = content,
            TokenCount = content.Length / 4,
            Embedding = new Vector(embedding),
            EmbeddingModel = "all-minilm:l6-v2",
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static float[] UnitAt(int index)
    {
        var vector = new float[DocumentChunk.EmbeddingDimension];
        vector[index] = 1f;
        return vector;
    }

    private static float[] TestEmbedding(float value)
    {
        var embedding = new float[DocumentChunk.EmbeddingDimension];
        embedding[0] = value;
        return embedding;
    }

    [Test]
    public async Task SearchAsync_ApprovedSharedFolderOfOtherUser_RetrievesChunks()
    {
        using var db = CreateDb();
        var me = SeedUser(db);
        var other = SeedUser(db);
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = other.Id,
            Name = "Approved Share",
            ShareStatus = FolderStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Folders.Add(folder);

        var doc = SeedDocument(db, other.Id, "shared.pdf", folder.Id);
        SeedChunk(db, doc, 0, "shared content to search", UnitAt(0));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(
            me.SupabaseUserId,
            new RagSearchRequest("search", null, folder.Id, null, null, TopK: 5),
            CancellationToken.None);

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(doc.Id);
    }

    [Test]
    public async Task SearchAsync_NonApprovedSharedFolderOfOtherUser_ExcludesChunks()
    {
        using var db = CreateDb();
        var me = SeedUser(db);
        var other = SeedUser(db);
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = other.Id,
            Name = "Private Share",
            ShareStatus = FolderStatus.None,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Folders.Add(folder);

        var doc = SeedDocument(db, other.Id, "private.pdf", folder.Id);
        SeedChunk(db, doc, 0, "private content to search", UnitAt(0));
        await db.SaveChangesAsync();

        var sut = BuildSut(db, UnitAt(0));

        var results = await sut.SearchAsync(
            me.SupabaseUserId,
            new RagSearchRequest("search", null, folder.Id, null, null, TopK: 5),
            CancellationToken.None);

        results.Should().BeEmpty();
    }
}
