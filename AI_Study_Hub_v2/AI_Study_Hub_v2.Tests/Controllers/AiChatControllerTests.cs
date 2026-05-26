using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class AiChatControllerTests
{
    private static AiChatController BuildSut(IAiChatService service, ClaimsPrincipal? user = null)
    {
        var ctrl = new AiChatController(service, NullLogger<AiChatController>.Instance);
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
        ok.Value.Should().BeSameAs(response);
        capturedUserId.Should().Be(supabaseUserId);
        service.VerifyAll();
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
}
