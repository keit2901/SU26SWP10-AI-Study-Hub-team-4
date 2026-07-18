using AI_Study_Hub_v2.Services;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AdminAccessPolicyTests
{
    [TestCase("Admin")]
    [TestCase("admin")]
    [TestCase("ADMIN")]
    public void IsAdmin_AdminRole_IsCaseInsensitive(string role)
    {
        AdminAccessPolicy.IsAdmin(role).Should().BeTrue();
        AdminAccessPolicy.GetAuthenticatedLandingPage(role).Should().Be("/admin");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Student")]
    public void IsAdmin_NonAdminRole_IsRejected(string? role)
    {
        AdminAccessPolicy.IsAdmin(role).Should().BeFalse();
        AdminAccessPolicy.GetAuthenticatedLandingPage(role).Should().Be("/documents");
    }

    [TestCase("Moderator")]
    [TestCase("moderator")]
    [TestCase("MODERATOR")]
    public void GetAuthenticatedLandingPage_ModeratorRole_ReturnsDashboard(string role)
    {
        AdminAccessPolicy.IsAdmin(role).Should().BeFalse();
        AdminAccessPolicy.GetAuthenticatedLandingPage(role).Should().Be("/dashboard");
    }
}
