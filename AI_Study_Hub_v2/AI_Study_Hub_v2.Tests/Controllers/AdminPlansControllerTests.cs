using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class AdminPlansControllerTests
{
    private Mock<IPlanService> _planServiceMock = null!;
    private Mock<IStorageQuotaService> _quotaServiceMock = null!;
    private Mock<IStorageReconciliationService> _reconciliationServiceMock = null!;
    private Mock<IAuditLogService> _auditServiceMock = null!;

    [SetUp]
    public void SetUp()
    {
        _planServiceMock = new Mock<IPlanService>();
        _quotaServiceMock = new Mock<IStorageQuotaService>();
        _reconciliationServiceMock = new Mock<IStorageReconciliationService>();
        _auditServiceMock = new Mock<IAuditLogService>();
    }

    private AdminPlansController BuildSut(AppDbContext db, ClaimsPrincipal? user = null)
    {
        var ctrl = new AdminPlansController(
            db,
            _planServiceMock.Object,
            _quotaServiceMock.Object,
            _reconciliationServiceMock.Object,
            _auditServiceMock.Object,
            NullLogger<AdminPlansController>.Instance);

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

    private static Plan CreatePlan(string key, string displayName, int sortOrder, bool isActive = true)
    {
        return new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = key,
            DisplayName = displayName,
            SortOrder = sortOrder,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Test]
    public async Task GetAllPlans_ReturnsAllPlansOrderedBySortOrder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var p1 = CreatePlan("pro", "Pro", 2, isActive: true);
        var p2 = CreatePlan("free", "Free", 1, isActive: true);
        db.Plans.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var sut = BuildSut(db);

        var result = await sut.GetAllPlans(CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = okResult.Value.Should().BeAssignableTo<IEnumerable<PlanDto>>().Subject;
        list.Should().HaveCount(2);
        list.First().PlanKey.Should().Be("free");
    }

    [Test]
    public async Task UpdatePlan_HappyPath_UpdatesDatabase_AndAudits()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var plan = CreatePlan("free", "Free", 1);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = BuildSut(db, Principal(adminId));

        var request = new UpdatePlanRequest
        {
            StorageQuotaBytes = 5000,
            MaxDocumentCount = 10,
            MaxFolderCount = 5,
            DailyTokenQuota = 200,
            MaxFileSizeBytes = 1000,
            MaxDocsPerFolder = 2
        };

        var result = await sut.UpdatePlan(plan.Id, request, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<PlanDto>().Subject;
        dto.StorageQuotaBytes.Should().Be(5000);

        // Verify database updated
        var updatedPlan = await db.Plans.FindAsync(plan.Id);
        updatedPlan!.StorageQuotaBytes.Should().Be(5000);

        // Verify audit log added
        _auditServiceMock.Verify(a => a.Add(
            adminId,
            "UpdatePlan",
            "Plan",
            plan.Id.ToString(),
            "Medium",
            It.IsAny<string>(),
            It.IsAny<string>(),
            null,
            null,
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task GetUserPlan_HappyPath_ReturnsOk_WithUserPlanDetails()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid(),
            Username = "user1",
            FullName = "User One",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var plan = CreatePlan("pro", "Pro", 2);
        var userPlan = new UserPlan
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlanId = plan.Id,
            Status = "active",
            AssignedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        db.Plans.Add(plan);
        db.UserPlans.Add(userPlan);
        await db.SaveChangesAsync();

        var snapshot = new StorageQuotaSnapshotDto(100, 1000, "pro", "Pro Plan");
        _quotaServiceMock
            .Setup(q => q.GetSnapshotAsync(user.SupabaseUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var sut = BuildSut(db);

        var result = await sut.GetUserPlan(user.Id, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<UserPlanDto>().Subject;
        dto.PlanKey.Should().Be("pro");
        dto.Status.Should().Be("active");
    }

    [Test]
    public async Task AssignPlan_HappyPath_DeactivatesExisting_CreatesNew_AndAudits()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid(),
            Username = "user1",
            FullName = "User One",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var oldPlan = CreatePlan("free", "Free", 1);
        var newPlan = CreatePlan("pro", "Pro", 2);
        var existingActive = new UserPlan
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlanId = oldPlan.Id,
            Status = "active",
            AssignedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        db.Users.Add(user);
        db.Plans.AddRange(oldPlan, newPlan);
        db.UserPlans.Add(existingActive);
        await db.SaveChangesAsync();

        _planServiceMock.Setup(s => s.GetPlanByKey("pro")).Returns(newPlan);
        var snapshot = new StorageQuotaSnapshotDto(200, 2000, "pro", "Pro Plan");
        _quotaServiceMock
            .Setup(q => q.GetSnapshotAsync(user.SupabaseUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var adminId = Guid.NewGuid();
        var sut = BuildSut(db, Principal(adminId));

        var request = new AssignPlanRequest("pro");
        var result = await sut.AssignPlan(user.Id, request, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<UserPlanDto>().Subject;
        dto.PlanKey.Should().Be("pro");

        // Verify old plan deactivated
        existingActive.Status.Should().Be("deactivated");

        // Verify new plan created
        var currentActive = db.UserPlans.Single(up => up.UserId == user.Id && up.Status == "active");
        currentActive.PlanId.Should().Be(newPlan.Id);

        // Verify audit log added
        _auditServiceMock.Verify(a => a.Add(
            adminId,
            "ManualPlanAssignment",
            "UserPlan",
            currentActive.Id.ToString(),
            "Medium",
            It.IsAny<string>(),
            It.IsAny<string>(),
            null,
            null,
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ReconcileStorage_HappyPath_TriggersService_AndAudits()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var discrepancies = new List<StorageDiscrepancy>
        {
            new(Guid.NewGuid(), "user1", 100, 200, 100)
        };
        _reconciliationServiceMock
            .Setup(r => r.ReconcileAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(discrepancies);

        var adminId = Guid.NewGuid();
        var sut = BuildSut(db, Principal(adminId));

        var result = await sut.ReconcileStorage(CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(discrepancies);

        // Verify audit log added
        _auditServiceMock.Verify(a => a.Add(
            adminId,
            "StorageReconciliation",
            "Storage",
            null,
            "Low",
            null,
            null,
            It.IsAny<string>(),
            null,
            It.IsAny<string>()), Times.Once);
    }
}
