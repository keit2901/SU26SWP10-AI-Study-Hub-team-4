using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class PlansControllerTests
{
    private Mock<IPlanService> _planServiceMock = null!;
    private Mock<IStorageQuotaService> _quotaServiceMock = null!;
    private AppDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        _planServiceMock = new Mock<IPlanService>();
        _quotaServiceMock = new Mock<IStorageQuotaService>();
        _db = TestDb.CreateInMemory();
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    private PlansController BuildSut(ClaimsPrincipal? user = null)
    {
        var ctrl = new PlansController(
            _planServiceMock.Object,
            _quotaServiceMock.Object,
            _db,
            NullLogger<PlansController>.Instance);

        var http = new DefaultHttpContext();
        if (user is not null)
        {
            http.User = user;
        }
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static ClaimsPrincipal Principal(Guid? supabaseUserId = null)
    {
        var claims = new List<Claim>();
        if (supabaseUserId.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, supabaseUserId.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    [Test]
    public void GetPlans_ReturnsOk_WithActivePlansMappedToDtos()
    {
        var plans = new List<Plan>
        {
            new() { PlanKey = "free", DisplayName = "Free Plan", IsActive = true },
            new() { PlanKey = "pro", DisplayName = "Pro Plan", IsActive = true }
        };
        _planServiceMock.Setup(s => s.GetActivePlans()).Returns(plans);

        var sut = BuildSut();

        var result = sut.GetPlans();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedPlans = okResult.Value.Should().BeAssignableTo<IEnumerable<PlanDto>>().Subject;
        returnedPlans.Should().HaveCount(2);
        returnedPlans.First().PlanKey.Should().Be("free");
    }

    [Test]
    public async Task GetCurrentPlan_HappyPath_ReturnsOk_WithSnapshot()
    {
        var userId = Guid.NewGuid();
        var snapshot = new StorageQuotaSnapshotDto(100, 1000, "pro", "Pro Plan");
        _quotaServiceMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var sut = BuildSut(Principal(userId));

        var result = await sut.GetCurrentPlan(CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(snapshot);
    }

    [Test]
    public async Task GetCurrentPlan_Exception_Returns500InternalServerError()
    {
        var userId = Guid.NewGuid();
        _quotaServiceMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database down"));

        var sut = BuildSut(Principal(userId));

        var result = await sut.GetCurrentPlan(CancellationToken.None);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        var err = statusResult.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        err.Code.Should().Be("unexpected_error");
    }
}
