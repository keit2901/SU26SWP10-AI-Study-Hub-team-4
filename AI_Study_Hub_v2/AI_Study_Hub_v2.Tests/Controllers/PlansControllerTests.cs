using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AI_Study_Hub_v2.Controllers;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Controllers;

[TestFixture]
public class PlansControllerTests
{
    private Mock<IPlanService> _planServiceMock = null!;
    private Mock<IStorageQuotaService> _quotaServiceMock = null!;
    private Mock<IVnPayService> _vnPayServiceMock = null!;
    private Mock<IAuditLogService> _auditServiceMock = null!;
    private AppDbContext _db = null!;

    [SetUp]
    public void SetUp()
    {
        _planServiceMock = new Mock<IPlanService>();
        _quotaServiceMock = new Mock<IStorageQuotaService>();
        _vnPayServiceMock = new Mock<IVnPayService>();
        _auditServiceMock = new Mock<IAuditLogService>();
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
            _vnPayServiceMock.Object,
            _db,
            _auditServiceMock.Object,
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

    // ── PurchasePlan tests ──

    [Test]
    public async Task GetMyPaymentTransactions_ReturnsOnlyCurrentUsersTransactions()
    {
        var supabaseUserId = Guid.NewGuid();
        SeedUser(supabaseUserId);
        var user = await _db.Users.SingleAsync(u => u.SupabaseUserId == supabaseUserId);

        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid(),
            Username = "otheruser",
            FullName = "Other User",
            RoleId = 2,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Users.Add(otherUser);

        _db.PaymentTransactions.AddRange(
            new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TxnRef = "VP_student_1",
                PlanKey = "pro",
                BillingCycle = "monthly",
                AmountVnd = 50_000,
                Status = "completed",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            },
            new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                UserId = otherUser.Id,
                TxnRef = "VP_other_1",
                PlanKey = "unlimited",
                BillingCycle = "yearly",
                AmountVnd = 1_000_000,
                Status = "completed",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        await _db.SaveChangesAsync();

        var sut = BuildSut(Principal(supabaseUserId));

        var result = await sut.GetMyPaymentTransactions(12, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var transactions = okResult.Value.Should().BeAssignableTo<IReadOnlyList<PaymentTransactionDto>>().Subject;
        transactions.Should().HaveCount(1);
        transactions[0].UserId.Should().Be(user.Id);
        transactions[0].UserName.Should().Be(user.Username);
        transactions[0].PlanKey.Should().Be("pro");
    }

    [Test]
    public async Task PurchasePlan_InvalidPlanKey_ReturnsNotFound()
    {
        var supabaseUserId = Guid.NewGuid();
        _planServiceMock.Setup(s => s.GetPlanByKey("nonexistent")).Returns((Plan?)null);

        SeedUser(supabaseUserId);
        var sut = BuildSut(Principal(supabaseUserId));

        var request = new PurchasePlanRequest("nonexistent");
        var result = await sut.PurchasePlan(request, CancellationToken.None);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var err = notFound.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        err.Code.Should().Be("plan_not_found");
    }

    [Test]
    [Ignore("Validated at HTTP pipeline level by InvalidModelStateResponseFactory. DTO-level validation on positional records is not testable via Validator.TryValidateObject.")]
    public void PurchasePlanRequest_InvalidBillingCycle_FailsValidation()
    {
        // The PurchasePlanRequest DTO has [RegularExpression(@"^(monthly|yearly)$")]
        // on BillingCycle. [ApiController] applies this automatically via the
        // HTTP pipeline. In unit tests we verify the DTO validation directly.
        var request = new PurchasePlanRequest("pro", "weekly");

        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("BillingCycle"));
    }

    [Test]
    public async Task PurchasePlan_Unauthenticated_ReturnsInternalServerError()
    {
        // Without a ClaimsPrincipal, GetSupabaseUserIdFromClaims throws
        // InvalidOperationException, which is caught by the catch-all and returns 500.
        // In production, [Authorize] at the middleware level returns 401 before
        // the controller method runs. This test validates the fallback behavior.
        var sut = BuildSut(); // no ClaimsPrincipal

        var request = new PurchasePlanRequest("pro");
        var ct = CancellationToken.None;
        var result = await sut.PurchasePlan(request, ct);

        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Test]
    public async Task PurchasePlan_PaidPlan_ReturnsPaymentUrlResponse()
    {
        var supabaseUserId = Guid.NewGuid();
        SeedUser(supabaseUserId);

        var dbPlan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = "pro",
            DisplayName = "Pro Plan",
            MonthlyPriceVnd = 49_000,
            YearlyPriceVnd = 490_000,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Plans.Add(dbPlan);
        await _db.SaveChangesAsync();

        _planServiceMock.Setup(s => s.GetPlanByKey("pro")).Returns(dbPlan);

        var expectedResponse = new PaymentUrlResponse(
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?test=1",
            "VP_test_txn_ref",
            "pro",
            "monthly",
            49_000,
            DateTimeOffset.UtcNow.AddMinutes(15));

        _vnPayServiceMock
            .Setup(s => s.CreatePaymentAsync(
                It.IsAny<Guid>(), "pro", "monthly", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var sut = BuildSut(Principal(supabaseUserId));

        var request = new PurchasePlanRequest("pro");
        var result = await sut.PurchasePlan(request, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<PaymentUrlResponse>().Subject;
        dto.PlanKey.Should().Be("pro");
        dto.AmountVnd.Should().Be(49_000);
        dto.PaymentUrl.Should().NotBeNullOrEmpty();
        dto.TxnRef.Should().Be("VP_test_txn_ref");

        // Verify VnPayService was called (DB writes happen inside VnPayService)
        _vnPayServiceMock.Verify(
            s => s.CreatePaymentAsync(It.IsAny<Guid>(), "pro", "monthly", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify audit was logged for payment initiation
        _auditServiceMock.Verify(a => a.Add(
            supabaseUserId,
            "PlanPaymentInitiated",
            "PaymentTransaction",
            dbPlan.Id.ToString(),
            "Low",
            null,
            null,
            It.Is<string?>(s => s != null && s.Contains("planKey")),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task PurchasePlan_FreePlan_ReturnsUserPlanDto()
    {
        var supabaseUserId = Guid.NewGuid();
        SeedUser(supabaseUserId);

        var dbPlan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = "free",
            DisplayName = "Free Plan",
            MonthlyPriceVnd = 0,
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Plans.Add(dbPlan);
        await _db.SaveChangesAsync();

        _planServiceMock.Setup(s => s.GetPlanByKey("free")).Returns(dbPlan);
        _quotaServiceMock
            .Setup(s => s.GetSnapshotAsync(supabaseUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageQuotaSnapshotDto(0, 10L * 1024 * 1024 * 1024, "free", "Free Plan"));

        var sut = BuildSut(Principal(supabaseUserId));

        var request = new PurchasePlanRequest("free");
        var result = await sut.PurchasePlan(request, CancellationToken.None);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<UserPlanDto>().Subject;
        dto.PlanKey.Should().Be("free");
        dto.Status.Should().Be("active");

        // Verify UserPlan was created immediately for free plan
        var userPlan = await _db.UserPlans.FirstOrDefaultAsync();
        userPlan.Should().NotBeNull();
        userPlan!.Status.Should().Be("active");

        // Verify PaymentTransaction with completed status (not demo_completed)
        var txn = await _db.PaymentTransactions.FirstOrDefaultAsync();
        txn.Should().NotBeNull();
        txn!.Status.Should().Be("completed");
        txn.AmountVnd.Should().Be(0);

        // Verify VNPay was NOT called for free plan
        _vnPayServiceMock.Verify(
            s => s.CreatePaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Helpers ──

    private void SeedUser(Guid supabaseUserId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = supabaseUserId,
            Username = "testuser",
            FullName = "Test User",
            RoleId = 2, // Student
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
    }
}
