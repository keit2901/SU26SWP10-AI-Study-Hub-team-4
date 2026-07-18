using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class AuditLogsControllerTests
{
    [Test]
    public async Task List_WithValidParams_ReturnsOkWithFilteredLogs()
    {
        var mock = new Mock<IAuditLogService>();
        var logs = new List<AuditLogDto>
        {
            new(Guid.NewGuid(), null, "System", "LOGIN_FAILED", "User", "user-1",
                "High", null, null, null, "127.0.0.1", "req-1", DateTimeOffset.UtcNow)
        };
        mock.Setup(s => s.ListAsync(null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        var sut = new AuditLogsController(mock.Object);

        var result = await sut.List(null, null, null, 200, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeEquivalentTo(logs);
    }

    [Test]
    public async Task List_WithActionFilter_CallsServiceWithActionParam()
    {
        var mock = new Mock<IAuditLogService>();
        mock.Setup(s => s.ListAsync("ROLE_CHANGE", null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditLogDto>());

        var sut = new AuditLogsController(mock.Object);

        await sut.List(action: "ROLE_CHANGE", from: null, to: null, limit: 200, null, CancellationToken.None);

        mock.Verify(s => s.ListAsync("ROLE_CHANGE", null, null, 200, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task List_WithLimit_CallsServiceWithLimit()
    {
        var mock = new Mock<IAuditLogService>();
        mock.Setup(s => s.ListAsync(null, null, null, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditLogDto>());

        var sut = new AuditLogsController(mock.Object);

        await sut.List(action: null, from: null, to: null, limit: 50, null, CancellationToken.None);

        mock.Verify(s => s.ListAsync(null, null, null, 50, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
