using System.Reflection;
using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class CommunityControllerTests
{
    [TestCase(nameof(CommunityController.GetPendingReports))]
    [TestCase(nameof(CommunityController.ResolveReport))]
    public void ReviewEndpoint_RequiresAdminOrModeratorRole(string methodName)
    {
        var method = typeof(CommunityController).GetMethod(methodName);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Admin,Moderator");
    }

    [Test]
    public async Task ReportFolder_ValidRequest_Returns201AndForwardsClaim()
    {
        var supabaseUserId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        Guid? capturedUserId = null;
        var service = new Mock<ICommunityService>();
        service.Setup(item => item.ReportFolderAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, CancellationToken>((userId, _, _, _) => capturedUserId = userId)
            .ReturnsAsync(reportId);
        var sut = BuildSut(service.Object, supabaseUserId);

        var result = await sut.ReportFolder(
            new CreateReportRequest(Guid.NewGuid(), "Please review"),
            CancellationToken.None);

        var response = result.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.Value.Should().Be(reportId);
        capturedUserId.Should().Be(supabaseUserId);
    }

    private static CommunityController BuildSut(ICommunityService service, Guid supabaseUserId)
    {
        var controller = new CommunityController(
            service,
            NullLogger<CommunityController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, supabaseUserId.ToString()) },
                    "Bearer")),
            },
        };
        return controller;
    }
}
