using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class AiChatControllerTests
{
    private static AiChatController BuildSut(IAiChatService service, ClaimsPrincipal? user = null, IChatPersistenceService? persistence = null)
    {
        if (persistence is null)
        {
            var mock = new Mock<IChatPersistenceService>(MockBehavior.Loose);
            mock.Setup(p => p.CreateSessionAsync(It.IsAny<Guid>(), It.IsAny<CreateChatSessionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatSessionDto { Id = Guid.NewGuid() });
            mock.Setup(p => p.GetMessagesScopedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ChatMessageDto>());
            persistence = mock.Object;
        }
        var ctrl = new AiChatController(service, persistence, NullLogger<AiChatController>.Instance);
        var http = new DefaultHttpContext();
        if (user is not null)
        {
            http.User = user;
        }
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static ClaimsPrincipal Principal(Guid? supabaseUserId = null, bool useSubInsteadOfNameId = false)
    {
        var claims = new List<Claim>();
        if (supabaseUserId.HasValue)
        {
            claims.Add(new Claim(
                useSubInsteadOfNameId ? "sub" : ClaimTypes.NameIdentifier,
                supabaseUserId.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    [Test]
    public async Task Ask_HappyPath_Returns200_AndForwardsAuthenticatedUserId()
    {
        var supabaseUserId = Guid.NewGuid();
        var response = new AiChatAnswerResponse(
            "RAG retrieves chunks. [S1]",
            new[]
            {
                new AiChatSourceDto("S1", Guid.NewGuid(), "rag.pdf", 1, 2, "RAG retrieves chunks.", 0.9),
            });
        Guid? capturedUserId = null;

        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        service.Setup(s => s.AskAsync(
                It.IsAny<Guid>(),
                It.IsAny<AiChatAskRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, AiChatAskRequest, CancellationToken>((uid, _, _) => capturedUserId = uid)
            .ReturnsAsync(response);

        var sut = BuildSut(service.Object, Principal(supabaseUserId, useSubInsteadOfNameId: true));

        var result = await sut.Ask(new AiChatAskRequest("How does RAG work?", null, null, null, null), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var actual = ok.Value.Should().BeOfType<AiChatAnswerResponse>().Subject;
        actual.Answer.Should().Be(response.Answer);
        actual.Sources.Should().BeEquivalentTo(response.Sources);
        actual.SessionId.Should().NotBeNull();
        capturedUserId.Should().Be(supabaseUserId);
        service.VerifyAll();
    }

    [Test]
    public void Ask_AllowAnonymousMetadataIsNotPresent()
    {
        typeof(AiChatController)
            .GetCustomAttributes(inherit: false)
            .Should().NotContain(attribute => attribute.GetType().Name == "AllowAnonymousAttribute");
    }

    [Test]
    public async Task Ask_InvalidAuthClaim_Returns401_AndSkipsService()
    {
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        var sut = BuildSut(service.Object, Principal());

        var result = await sut.Ask(new AiChatAskRequest("How does RAG work?", null, null, null, null), CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(401);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_user_id");
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Ask_MissingBody_Returns400_AndSkipsService()
    {
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        var sut = BuildSut(service.Object, Principal(Guid.NewGuid()));

        var result = await sut.Ask(null!, CancellationToken.None);

        var bad = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("missing_body");
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Ask_ServiceValidationError_MapsStatusAndCode()
    {
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        service.Setup(s => s.AskAsync(
                It.IsAny<Guid>(),
                It.IsAny<AiChatAskRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatException(400, "question_required", "Question is required."));

        var sut = BuildSut(service.Object, Principal(Guid.NewGuid()));

        var result = await sut.Ask(new AiChatAskRequest("", null, null, null, null), CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(400);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("question_required");
        service.VerifyAll();
    }

    [Test]
    public async Task Ask_UnsupportedModel_MapsTo400()
    {
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        service.Setup(s => s.AskAsync(
                It.IsAny<Guid>(),
                It.IsAny<AiChatAskRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatException(400, "unsupported_model", "The requested AI model is not supported."));
        var sut = BuildSut(service.Object, Principal(Guid.NewGuid()));

        var result = await sut.Ask(new AiChatAskRequest("Question", null, null, null, null, Model: "unknown"), CancellationToken.None);

        var error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(400);
        error.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("unsupported_model");
        service.VerifyAll();
    }

    [Test]
    public async Task Ask_ProviderUnavailable_MapsTo503()
    {
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        service.Setup(s => s.AskAsync(
                It.IsAny<Guid>(),
                It.IsAny<AiChatAskRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatException(503, "ai_provider_unavailable", "provider unavailable"));

        var sut = BuildSut(service.Object, Principal(Guid.NewGuid()));

        var result = await sut.Ask(new AiChatAskRequest("How does RAG work?", null, null, null, null), CancellationToken.None);

        var obj = result.Result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(503);
        obj.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("ai_provider_unavailable");
        service.VerifyAll();
    }

    [Test]
    public async Task Ask_SessionScopeMismatch_SkipsAiAndPersistence()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        persistence.Setup(p => p.GetMessagesScopedAsync(userId, sessionId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatException(404, "session_not_found", "Chat session not found."));
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        var sut = BuildSut(service.Object, Principal(userId), persistence.Object);

        var result = await sut.Ask(new AiChatAskRequest("Question", null, Guid.NewGuid(), null, null, SessionId: sessionId), CancellationToken.None);

        var error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(404);
        error.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("session_not_found");
        service.VerifyNoOtherCalls();
        persistence.VerifyAll();
    }

    [Test]
    public async Task Ask_ValidScopedSession_PassesRequestFolderToPersistence()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var response = new AiChatAnswerResponse("Answer", Array.Empty<AiChatSourceDto>());
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        persistence.Setup(p => p.GetMessagesScopedAsync(userId, sessionId, folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatMessageDto>());
        persistence.Setup(p => p.SaveExchangeAsync(userId, sessionId, folderId, "Question", It.IsAny<string>(), response, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        service.Setup(s => s.AskAsync(userId, It.IsAny<AiChatAskRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var sut = BuildSut(service.Object, Principal(userId), persistence.Object);

        var result = await sut.Ask(new AiChatAskRequest("Question", null, folderId, null, null, SessionId: sessionId), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        persistence.VerifyAll();
        service.VerifyAll();
    }

    [Test]
    public async Task GetSessionMessages_AndDeleteSession_ForwardExactNullableFolderScope()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        persistence.Setup(p => p.GetMessagesScopedAsync(userId, sessionId, folderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ChatMessageDto>());
        persistence.Setup(p => p.DeleteSessionAsync(userId, sessionId, folderId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(Mock.Of<IAiChatService>(), Principal(userId), persistence.Object);

        var get = await sut.GetSessionMessages(sessionId, folderId, CancellationToken.None);
        var delete = await sut.DeleteSession(sessionId, folderId, CancellationToken.None);

        get.Result.Should().BeOfType<OkObjectResult>();
        delete.Should().BeOfType<NoContentResult>();
        persistence.VerifyAll();
    }

    [Test]
    public async Task GetSessionMessages_ScopeMismatch_ReturnsNonDisclosingSessionNotFound()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var persistence = new Mock<IChatPersistenceService>(MockBehavior.Strict);
        persistence.Setup(p => p.GetMessagesScopedAsync(userId, sessionId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiChatException(404, "session_not_found", "Chat session not found."));
        var sut = BuildSut(Mock.Of<IAiChatService>(), Principal(userId), persistence.Object);

        var result = await sut.GetSessionMessages(sessionId, null, CancellationToken.None);

        var error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(404);
        error.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("session_not_found");
        persistence.VerifyAll();
    }

    [Test]
    public async Task CreateSession_ForeignFolder_ReturnsNonDisclosingFolderNotFound_WithoutCreatingSession()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = "owner",
            FullName = "Owner",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var other = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = "other",
            FullName = "Other",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var foreignFolder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = other.Id,
            Name = "Foreign",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.AddRange(owner, other);
        db.Folders.Add(foreignFolder);
        await db.SaveChangesAsync();
        var service = new Mock<IAiChatService>(MockBehavior.Strict);
        var persistence = new ChatPersistenceService(db, NullLogger<ChatPersistenceService>.Instance, Mock.Of<IAuditLogService>());
        var sut = BuildSut(service.Object, Principal(owner.SupabaseUserId), persistence);

        var result = await sut.CreateSession(new CreateChatSessionRequest { FolderId = foreignFolder.Id }, CancellationToken.None);

        var error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(404);
        error.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("folder_not_found");
        db.ChatSessions.Should().BeEmpty();
        service.VerifyNoOtherCalls();
    }
}
