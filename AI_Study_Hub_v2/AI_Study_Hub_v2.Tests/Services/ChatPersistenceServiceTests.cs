using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class ChatPersistenceServiceTests
{
    [Test]
    public async Task GetMessagesScopedAsync_ReturnsMessagesOnlyForExactNullableFolderScope()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        var folderId = Guid.NewGuid();
        var session = await SeedSessionAsync(db, user, folderId);
        await SeedMessagesAsync(db, session.Id);
        var sut = CreateSut(db);

        var messages = await sut.GetMessagesScopedAsync(user.SupabaseUserId, session.Id, folderId);

        messages.Should().HaveCount(2);

        var act = () => sut.GetMessagesScopedAsync(user.SupabaseUserId, session.Id, null);
        var ex = await act.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("session_not_found");

        var aggregateSession = await SeedSessionAsync(db, user, null);
        await SeedMessagesAsync(db, aggregateSession.Id);
        var aggregateMessages = await sut.GetMessagesScopedAsync(user.SupabaseUserId, aggregateSession.Id, null);
        aggregateMessages.Should().HaveCount(2);
    }

    [Test]
    public async Task ListSessionsAsync_UsesExactNullableFolderScope_AndExcludesOtherUsers()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        var otherUser = SeedUser(db);
        var folderId = Guid.NewGuid();
        var otherFolderId = Guid.NewGuid();
        var aggregate = await SeedSessionAsync(db, user, null);
        var matchingFolder = await SeedSessionAsync(db, user, folderId);
        await SeedSessionAsync(db, user, otherFolderId);
        await SeedSessionAsync(db, otherUser, folderId);
        var sut = CreateSut(db);

        var aggregateSessions = await sut.ListSessionsAsync(user.SupabaseUserId, null);
        var folderSessions = await sut.ListSessionsAsync(user.SupabaseUserId, folderId);

        aggregateSessions.Select(s => s.Id).Should().Equal(aggregate.Id);
        folderSessions.Select(s => s.Id).Should().Equal(matchingFolder.Id);
    }

    [Test]
    public async Task DeleteSessionAsync_RequiresOwnerAndExactNullableFolderScope()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db);
        var other = SeedUser(db);
        var folderId = Guid.NewGuid();
        var folderSession = await SeedSessionAsync(db, owner, folderId);
        var aggregateSession = await SeedSessionAsync(db, owner, null);
        var foreignSession = await SeedSessionAsync(db, other, folderId);
        var sut = CreateSut(db);

        foreach (var delete in new Func<Task>[]
        {
            () => sut.DeleteSessionAsync(owner.SupabaseUserId, folderSession.Id, null),
            () => sut.DeleteSessionAsync(owner.SupabaseUserId, aggregateSession.Id, folderId),
            () => sut.DeleteSessionAsync(owner.SupabaseUserId, foreignSession.Id, folderId),
            () => sut.DeleteSessionAsync(owner.SupabaseUserId, Guid.NewGuid(), folderId),
        })
        {
            var ex = await delete.Should().ThrowAsync<AiChatException>();
            ex.Which.StatusCode.Should().Be(404);
            ex.Which.Code.Should().Be("session_not_found");
        }

        await sut.DeleteSessionAsync(owner.SupabaseUserId, folderSession.Id, folderId);

        db.ChatSessions.Should().ContainSingle(s => s.Id == aggregateSession.Id);
        db.ChatSessions.Should().ContainSingle(s => s.Id == foreignSession.Id);
    }

    [Test]
    public async Task SaveExchangeAsync_RejectsScopeMismatchWithoutWriting_AndWritesForMatchingScope()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        var folderId = Guid.NewGuid();
        var session = await SeedSessionAsync(db, user, folderId);
        var sut = CreateSut(db);
        var response = new AiChatAnswerResponse("Answer", Array.Empty<AiChatSourceDto>());

        var mismatch = () => sut.SaveExchangeAsync(user.SupabaseUserId, session.Id, null, "Question", "scope", response);
        var ex = await mismatch.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("session_not_found");
        db.ChatMessages.Should().BeEmpty();

        await sut.SaveExchangeAsync(user.SupabaseUserId, session.Id, folderId, "Question", "scope", response);

        var messages = await db.ChatMessages.OrderBy(m => m.SequenceNumber).ToListAsync();
        messages.Select(m => m.Role).Should().Equal("user", "assistant");
    }

    [Test]
    public async Task SaveQuizExchangeAsync_RevalidatesExactScopeImmediatelyBeforeWriting()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db);
        var folderId = Guid.NewGuid();
        var session = await SeedSessionAsync(db, user, folderId);
        var sut = CreateSut(db);

        var mismatch = () => sut.SaveQuizExchangeAsync(
            user.SupabaseUserId, session.Id, null, "scope", "Generate quiz", Guid.NewGuid(), "Quiz", "InProgress");
        var ex = await mismatch.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("session_not_found");
        db.ChatMessages.Should().BeEmpty();

        await sut.SaveQuizExchangeAsync(
            user.SupabaseUserId, session.Id, folderId, "scope", "Generate quiz", Guid.NewGuid(), "Quiz", "InProgress");

        db.ChatMessages.Should().HaveCount(2);
    }

    [Test]
    public async Task CreateSessionAsync_RejectsForeignFolderWithoutCreatingRow_AndAcceptsOwnedFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db);
        var other = SeedUser(db);
        var foreignFolder = new Folder { Id = Guid.NewGuid(), UserId = other.Id, Name = "Other", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var ownedFolder = new Folder { Id = Guid.NewGuid(), UserId = owner.Id, Name = "Mine", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Folders.AddRange(foreignFolder, ownedFolder);
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var rejected = () => sut.CreateSessionAsync(owner.SupabaseUserId, new CreateChatSessionRequest { FolderId = foreignFolder.Id });
        var ex = await rejected.Should().ThrowAsync<AiChatException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("folder_not_found");
        db.ChatSessions.Should().BeEmpty();

        var created = await sut.CreateSessionAsync(owner.SupabaseUserId, new CreateChatSessionRequest { FolderId = ownedFolder.Id });

        created.FolderId.Should().Be(ownedFolder.Id);
        db.ChatSessions.Should().ContainSingle(s => s.Id == created.Id && s.UserId == owner.Id);
    }

    private static AI_Study_Hub_v2.Services.ChatPersistenceService CreateSut(AppDbContext db) => new(db, NullLogger<AI_Study_Hub_v2.Services.ChatPersistenceService>.Instance);

    private static User SeedUser(AppDbContext db)
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

    private static async Task<ChatSession> SeedSessionAsync(AppDbContext db, User user, Guid? folderId)
    {
        var session = new ChatSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FolderId = folderId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    private static async Task SeedMessagesAsync(AppDbContext db, Guid sessionId)
    {
        db.ChatMessages.AddRange(
            new ChatMessage { Id = Guid.NewGuid(), ChatSessionId = sessionId, Role = "user", Content = "Question", SequenceNumber = 0, CreatedAt = DateTimeOffset.UtcNow },
            new ChatMessage { Id = Guid.NewGuid(), ChatSessionId = sessionId, Role = "assistant", Content = "Answer", SequenceNumber = 1, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}
