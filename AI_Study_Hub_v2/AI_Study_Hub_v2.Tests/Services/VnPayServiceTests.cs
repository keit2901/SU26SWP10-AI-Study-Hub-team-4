using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class VnPayServiceTests
{
    private AppDbContext _db = null!;
    private Mock<IPlanService> _planServiceMock = null!;
    private Mock<IAuditLogService> _auditMock = null!;
    private VnPaySettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _db = TestDb.CreateInMemory();
        _planServiceMock = new Mock<IPlanService>();
        _auditMock = new Mock<IAuditLogService>();
        _settings = new VnPaySettings
        {
            TmnCode = "TESTCODE",
            HashSecret = "test-secret",
            BaseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
            ReturnUrl = "http://localhost:5240/payment/result",
            ExpireMinutes = 15,
        };
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
    }

    [Test]
    public async Task CreatePaymentAsync_WhenMatchingPendingExists_ReusesTransaction()
    {
        var user = SeedUser();
        var plan = SeedPlan("pro", 50_000, 500_000, sortOrder: 2);
        var existingPending = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TxnRef = "VP_EXISTING_PENDING",
            PlanKey = "pro",
            BillingCycle = "monthly",
            AmountVnd = 50_000,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(13),
        };
        _db.PaymentTransactions.Add(existingPending);
        await _db.SaveChangesAsync();

        _planServiceMock.Setup(x => x.GetPlanByKey("pro")).Returns(plan);

        var sut = CreateSut();

        var result = await sut.CreatePaymentAsync(user.Id, "pro", "monthly", "127.0.0.1", CancellationToken.None);

        result.TxnRef.Should().Be(existingPending.TxnRef);
        result.AmountVnd.Should().Be(existingPending.AmountVnd);
        result.PaymentUrl.Should().Contain(existingPending.TxnRef);

        var transactions = await _db.PaymentTransactions
            .Where(x => x.UserId == user.Id)
            .ToListAsync();
        transactions.Should().HaveCount(1);
        transactions.Single().Status.Should().Be("pending");
    }

    [Test]
    public async Task CreatePaymentAsync_WhenDifferentPendingExists_ExpiresOldAndCreatesNewTransaction()
    {
        var user = SeedUser();
        var oldPlan = SeedPlan("pro", 50_000, 500_000, sortOrder: 2);
        var newPlan = SeedPlan("unlimited", 100_000, 1_000_000, sortOrder: 3);
        var existingPending = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TxnRef = "VP_OLD_PENDING_TXN",
            PlanKey = "pro",
            BillingCycle = "monthly",
            AmountVnd = 50_000,
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(14),
        };
        _db.PaymentTransactions.Add(existingPending);
        await _db.SaveChangesAsync();

        _planServiceMock.Setup(x => x.GetPlanByKey("pro")).Returns(oldPlan);
        _planServiceMock.Setup(x => x.GetPlanByKey("unlimited")).Returns(newPlan);

        var sut = CreateSut();

        var result = await sut.CreatePaymentAsync(user.Id, "unlimited", "monthly", "127.0.0.1", CancellationToken.None);

        result.TxnRef.Should().NotBe(existingPending.TxnRef);
        result.PlanKey.Should().Be("unlimited");
        result.AmountVnd.Should().Be(100_000);

        var transactions = await _db.PaymentTransactions
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        transactions.Should().HaveCount(2);
        transactions[0].Status.Should().Be("expired");
        transactions[0].ErrorMessage.Should().Be("Payment superseded by a new purchase attempt.");
        transactions[1].Status.Should().Be("pending");
        transactions[1].PlanKey.Should().Be("unlimited");
    }

    private VnPayService CreateSut()
    {
        return new VnPayService(
            _db,
            Microsoft.Extensions.Options.Options.Create(_settings),
            _planServiceMock.Object,
            _auditMock.Object,
            NullLogger<VnPayService>.Instance);
    }

    private User SeedUser()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid(),
            Username = $"user_{Guid.NewGuid():N}"[..12],
            FullName = "Test User",
            RoleId = 2,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private Plan SeedPlan(string planKey, long monthlyPriceVnd, long yearlyPriceVnd, int sortOrder)
    {
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = planKey,
            DisplayName = char.ToUpper(planKey[0]) + planKey[1..],
            MonthlyPriceVnd = monthlyPriceVnd,
            YearlyPriceVnd = yearlyPriceVnd,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Plans.Add(plan);
        _db.SaveChanges();
        return plan;
    }
}
