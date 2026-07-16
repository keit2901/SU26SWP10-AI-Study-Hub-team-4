using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class QuizControllerTests
{
    [Test]
    public async Task Save_ReturnsServerAuthoritativeQuiz()
    {
        var userId = Guid.NewGuid();
        var quizId = Guid.NewGuid();
        var expected = new QuizDto(quizId, "Quiz", QuizStatus.InProgress, 0, 1, Array.Empty<QuizQuestionDto>(),
            new Dictionary<int, string?> { [0] = "A" }, new Dictionary<int, bool> { [0] = true }, null, DateTimeOffset.UtcNow);
        var service = new Mock<IQuizService>(MockBehavior.Strict);
        service.Setup(s => s.SaveAsync(userId, quizId, It.IsAny<SaveQuizRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var sut = new QuizController(service.Object, NullLogger<QuizController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) })),
                },
            },
        };

        var result = await sut.Save(quizId, new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(expected);
        service.VerifyAll();
    }

    [Test]
    public async Task Generate_UnsupportedModel_Returns400WithUnsupportedModelCode()
    {
        var userId = Guid.NewGuid();
        var service = new Mock<IQuizService>(MockBehavior.Strict);
        service.Setup(s => s.GenerateAsync(userId, It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QuizException(400, "unsupported_model", "The requested AI model is not supported."));
        var sut = new QuizController(service.Object, NullLogger<QuizController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) })),
                },
            },
        };

        var result = await sut.Generate(new GenerateQuizRequest(Guid.NewGuid()), CancellationToken.None);

        var error = result.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(400);
        error.Value.Should().BeOfType<ApiErrorResponse>().Which.Code.Should().Be("unsupported_model");
        service.VerifyAll();
    }
}
