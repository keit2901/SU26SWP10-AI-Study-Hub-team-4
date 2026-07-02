using System.Reflection;
using AI_Study_Hub_v2.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public sealed class AdminAuthorizationTests
{
    [TestCase(typeof(AdminUsersController))]
    [TestCase(typeof(AuditLogsController))]
    public void AdminController_RequiresAdminRole(Type controllerType)
    {
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Admin");
    }

    [TestCase(nameof(CommunityController.GetPendingReports))]
    [TestCase(nameof(CommunityController.ResolveReport))]
    public void CommunityReviewEndpoint_RequiresReviewerRole(string methodName)
    {
        var method = typeof(CommunityController).GetMethod(methodName);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Admin,Moderator");
    }
}
