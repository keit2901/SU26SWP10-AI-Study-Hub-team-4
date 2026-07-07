using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class SystemSettingsControllerTests
{
    [Test]
    public async Task GetAll_HappyPath_Returns200_AndConfigList()
    {
        var mock = new Mock<ISystemConfigService>();
        var configs = new List<SystemConfigDto>
        {
            new("ai.chat_model", "gpt-4o-mini", "gpt-4o-mini", "Model", "Chat model", null, "Text", true, null, null, DateTimeOffset.UtcNow)
        };
        mock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(configs);

        var sut = BuildSut(mock.Object);

        var result = await sut.GetAll(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeEquivalentTo(configs);
    }

    [Test]
    public async Task Update_HappyPath_Returns200_AndUpdatedDto()
    {
        var mock = new Mock<ISystemConfigService>();
        var updated = new SystemConfigDto("ai.chat_model", "claude-3-5-sonnet", "gpt-4o-mini", "Model", "Chat model", null, "Text", true, DateTimeOffset.UtcNow, "admin@test.edu", DateTimeOffset.UtcNow);
        mock.Setup(s => s.UpdateValueAsync("ai.chat_model", "claude-3-5-sonnet", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var sut = BuildSut(mock.Object);

        var result = await sut.Update("ai.chat_model", new UpdateSystemConfigRequest { Value = "claude-3-5-sonnet" }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeEquivalentTo(updated);
    }

    [Test]
    public async Task Update_ConfigNotFound_Returns404()
    {
        var mock = new Mock<ISystemConfigService>();
        mock.Setup(s => s.UpdateValueAsync("nonexistent.key", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AdminException(404, "config_not_found", "System config 'nonexistent.key' not found."));

        var sut = BuildSut(mock.Object);

        var result = await sut.Update("nonexistent.key", new UpdateSystemConfigRequest { Value = "value" }, CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var error = objectResult.Value.Should().BeAssignableTo<ApiErrorResponse>().Subject;
        error.Code.Should().Be("config_not_found");
    }

    [Test]
    public async Task Update_UnexpectedError_Returns500()
    {
        var mock = new Mock<ISystemConfigService>();
        mock.Setup(s => s.UpdateValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection lost"));

        var sut = BuildSut(mock.Object);

        var result = await sut.Update("ai.chat_model", new UpdateSystemConfigRequest { Value = "value" }, CancellationToken.None);

        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var error = objectResult.Value.Should().BeAssignableTo<ApiErrorResponse>().Subject;
        error.Code.Should().Be("unexpected_error");
    }

    private static SystemSettingsController BuildSut(ISystemConfigService config)
    {
        var ctrl = new SystemSettingsController(config, NullLogger<SystemSettingsController>.Instance);
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, "admin@test.edu"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("sub", Guid.NewGuid().ToString())
        }, "TestAuth"));
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }
}
