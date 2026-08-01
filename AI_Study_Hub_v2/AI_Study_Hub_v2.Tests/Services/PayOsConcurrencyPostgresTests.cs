using System.Collections.Concurrent;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Payment;
using AI_Study_Hub_v2.Services.Payment.Abstractions;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

/// <summary>Opt-in PostgreSQL evidence for PayOS finalization locking and rollback behavior.</summary>
[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class PayOsConcurrencyPostgresTests
{
    private string _connectionString = null!;
    private NpgsqlDataSource? _dataSource;
    private readonly ConcurrentBag<Guid> _paymentIds = [];
    private readonly ConcurrentBag<Guid> _userPlanIds = [];
    private readonly ConcurrentBag<Guid> _planIds = [];
    private readonly ConcurrentBag<Guid> _userIds = [];
    private readonly ConcurrentBag<Guid> _authUserIds = [];
    private bool _bootstrapCompleted;

    [SetUp]
    public async Task RequireDedicatedTestDatabaseAsync()
    {
        _paymentIds.Clear();
        _userPlanIds.Clear();
        _planIds.Clear();
        _userIds.Clear();
        _authUserIds.Clear();
        _bootstrapCompleted = false;
        _connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
            Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Refusing PayOS concurrency tests outside a database ending in _test.");

        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();
        try
        {
            await BootstrapAuthAsync();
            await using var db = CreateDb();
            await PostgresTestDatabase.BootstrapAsync(db, new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);
            _bootstrapCompleted = true;
        }
        catch
        {
            var dataSource = _dataSource;
            _dataSource = null;
            if (dataSource is not null)
            {
                try { await dataSource.DisposeAsync(); }
                catch { /* Preserve the original setup/bootstrap failure. */ }
            }
            throw;
        }
    }

    [TearDown]
    public async Task CleanCreatedRowsAsync()
    {
        try
        {
            if (_bootstrapCompleted && _dataSource is not null)
            {
                await using var db = CreateDb();
                db.PaymentTransactions.RemoveRange(await db.PaymentTransactions.Where(item => _paymentIds.Contains(item.Id)).ToListAsync());
                db.UserPlans.RemoveRange(await db.UserPlans.Where(item => _userPlanIds.Contains(item.Id) || _userIds.Contains(item.UserId)).ToListAsync());
                db.Plans.RemoveRange(await db.Plans.Where(item => _planIds.Contains(item.Id)).ToListAsync());
                db.Users.RemoveRange(await db.Users.Where(item => _userIds.Contains(item.Id)).ToListAsync());
                await db.SaveChangesAsync();
                foreach (var authUserId in _authUserIds)
                    await DeleteAuthAsync(authUserId);
            }
        }
        finally
        {
            if (_dataSource is not null) await _dataSource.DisposeAsync();
            _dataSource = null;
            _bootstrapCompleted = false;
        }
    }

    [Test]
    public async Task WebhookAndReturnPaid_Race_CreatesExactlyOneEntitlement()
    {
        var scenario = await SeedAsync(paymentCount: 1);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var attempts = new[]
        {
            RunAsync(gate.Task, timeout.Token, scenario.Plan, new DeterministicProvider(scenario.OrderCodes[0], "PAID"),
                async service => await service.ReconcileAsync(scenario.UserId, scenario.OrderCodes[0], timeout.Token)),
            RunAsync(gate.Task, timeout.Token, scenario.Plan, new DeterministicProvider(scenario.OrderCodes[0], "PAID"),
                async service => await service.ProcessWebhookAsync(WebhookBody, timeout.Token)),
        };
        gate.SetResult(true);
        var results = await Task.WhenAll(attempts).WaitAsync(timeout.Token);

        results.Should().AllSatisfy(result => result.Exception.Should().BeNull("concurrent payment paths must not leak database exceptions"));
        await AssertCompletedPurchaseAsync(scenario, scenario.OrderCodes[0]);
        results.Select(result => result.Value).OfType<WebhookResult>()
            .Should().AllSatisfy(result => result.Disposition.Should().BeOneOf(WebhookDisposition.Accepted, WebhookDisposition.Idempotent));
        results.Select(result => result.Value).OfType<ReturnUrlResult>()
            .Should().AllSatisfy(result => result.Status.Should().Be("completed"));
    }

    [Test]
    public async Task PaidAndCancelled_Race_PaidDominates()
    {
        var scenario = await SeedAsync(paymentCount: 1);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var paid = RunAsync(gate.Task, timeout.Token, scenario.Plan, new DeterministicProvider(scenario.OrderCodes[0], "PAID"),
            async service => await service.ReconcileAsync(scenario.UserId, scenario.OrderCodes[0], timeout.Token));
        var cancelledProvider = new DeterministicProvider(scenario.OrderCodes[0], "PAID") { WebhookStatus = "CANCELLED" };
        var cancelled = RunAsync(gate.Task, timeout.Token, scenario.Plan, cancelledProvider,
            async service => await service.ProcessWebhookAsync(WebhookBody, timeout.Token));
        gate.SetResult(true);
        var results = await Task.WhenAll(paid, cancelled).WaitAsync(timeout.Token);

        results.Should().AllSatisfy(result => result.Exception.Should().BeNull("paid/cancelled races must not leak database exceptions"));
        await AssertCompletedPurchaseAsync(scenario, scenario.OrderCodes[0]);
        await using var fresh = CreateDb();
        (await fresh.PaymentTransactions.SingleAsync(item => item.ProviderOrderCode == scenario.OrderCodes[0])).Status.Should().Be("completed");
    }

    [Test]
    public async Task TwoPaidOrdersSameUser_Race_LeavesOneActivePlan()
    {
        var scenario = await SeedAsync(paymentCount: 2);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var attempts = scenario.OrderCodes.Select(orderCode => RunAsync(gate.Task, timeout.Token, scenario.Plan,
            new DeterministicProvider(orderCode, "PAID"),
            async service => await service.ReconcileAsync(scenario.UserId, orderCode, timeout.Token))).ToArray();
        gate.SetResult(true);
        var results = await Task.WhenAll(attempts).WaitAsync(timeout.Token);

        results.Should().AllSatisfy(result => result.Exception.Should().BeNull("separate order finalizations must serialize on the user row"));
        results.Should().AllSatisfy(result => ((ReturnUrlResult)result.Value!).Status.Should().Be("completed"));
        await using var fresh = CreateDb();
        var payments = await fresh.PaymentTransactions.Where(item => scenario.OrderCodes.Contains(item.ProviderOrderCode!.Value)).ToListAsync();
        payments.Should().HaveCount(2);
        payments.Should().OnlyContain(item => item.Status == "completed" && item.UserPlanId.HasValue);
        var plans = await fresh.UserPlans.Where(item => item.UserId == scenario.UserId).ToListAsync();
        plans.Should().HaveCount(2);
        plans.Count(item => item.Status == "active").Should().Be(1);
    }

    [Test]
    public async Task ActivationSaveFailure_RollsBackPaymentAndEntitlement()
    {
        var scenario = await SeedAsync(paymentCount: 1, includePriorActivePlan: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fault = new ActivationSaveFailureInterceptor();

        await using (var db = CreateDb(fault))
        {
            var service = CreateService(db, new DeterministicProvider(scenario.OrderCodes[0], "PAID"), scenario.Plan);
            var action = () => service.ReconcileAsync(scenario.UserId, scenario.OrderCodes[0], timeout.Token);
            await action.Should().ThrowAsync<DbUpdateException>();
        }

        fault.HitCount.Should().Be(1);
        await using var fresh = CreateDb();
        var payment = await fresh.PaymentTransactions.SingleAsync(item => item.ProviderOrderCode == scenario.OrderCodes[0]);
        payment.Status.Should().Be("pending");
        payment.UserPlanId.Should().BeNull();
        var plans = await fresh.UserPlans.Where(item => item.UserId == scenario.UserId).ToListAsync();
        plans.Should().ContainSingle(item => item.Id == scenario.PriorActivePlanId && item.Status == "active");
        plans.Should().HaveCount(1);
    }

    private async Task<AttemptResult> RunAsync(Task gate, CancellationToken ct, Plan plan, DeterministicProvider provider,
        Func<PaymentService, Task<object?>> action)
    {
        try
        {
            await gate.WaitAsync(ct);
            await using var db = CreateDb();
            return new AttemptResult(await action(CreateService(db, provider, plan)), null);
        }
        catch (Exception exception)
        {
            return new AttemptResult(null, exception);
        }
    }

    private PaymentService CreateService(AppDbContext db, DeterministicProvider provider, Plan? plan)
    {
        var plans = new Mock<IPlanService>();
        plans.Setup(item => item.GetPlanByKey(It.IsAny<string>())).Returns((string key) => plan is not null && key == plan.PlanKey ? plan : null);
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns(Environments.Development);
        return new PaymentService(provider, db, plans.Object, Mock.Of<IAuditLogService>(),
            Microsoft.Extensions.Options.Options.Create(new PayOsSettings { ExpireMinutes = 5 }), new ConfigurationBuilder().Build(), environment.Object,
            null, NullLogger<PaymentService>.Instance);
    }

    private async Task<Scenario> SeedAsync(int paymentCount, bool includePriorActivePlan = false)
    {
        await using var db = CreateDb();
        var roleId = (await db.Roles.SingleAsync(item => item.RoleName == Role.StudentRoleName)).Id;
        var userId = Guid.NewGuid();
        var authUserId = Guid.NewGuid();
        _userIds.Add(userId); _authUserIds.Add(authUserId);
        await InsertAuthAsync(authUserId);
        var plan = new Plan { Id = Guid.NewGuid(), PlanKey = $"payos-{Guid.NewGuid():N}", DisplayName = "PayOS test", IsActive = true, MonthlyPriceVnd = 100, YearlyPriceVnd = 1000, CreatedAt = DateTimeOffset.UtcNow };
        _planIds.Add(plan.Id);
        db.Add(new User { Id = userId, RoleId = roleId, SupabaseUserId = authUserId, Username = $"p{Guid.NewGuid():N}"[..15], FullName = "PayOS concurrency test", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.Plans.Add(plan);
        Guid? priorActivePlanId = null;
        if (includePriorActivePlan)
        {
            priorActivePlanId = Guid.NewGuid();
            _userPlanIds.Add(priorActivePlanId.Value);
            db.UserPlans.Add(new UserPlan { Id = priorActivePlanId.Value, UserId = userId, PlanId = plan.Id, Status = "active", AssignedAt = DateTimeOffset.UtcNow });
        }
        var orderCodes = Enumerable.Range(0, paymentCount).Select(index => 8_000_000_000_000L + Random.Shared.NextInt64(1_000_000) + index).ToArray();
        foreach (var orderCode in orderCodes)
        {
            var payment = new PaymentTransaction { Id = Guid.NewGuid(), UserId = userId, TxnRef = $"PO_{orderCode}", ProviderOrderCode = orderCode, ProviderPaymentLinkId = "test-link", PlanKey = plan.PlanKey, BillingCycle = "monthly", AmountVnd = 100, Status = "pending", CreatedAt = DateTimeOffset.UtcNow };
            _paymentIds.Add(payment.Id);
            db.PaymentTransactions.Add(payment);
        }
        await db.SaveChangesAsync();
        return new Scenario(userId, plan, orderCodes, priorActivePlanId);
    }

    private async Task AssertCompletedPurchaseAsync(Scenario scenario, long orderCode)
    {
        await using var fresh = CreateDb();
        var payment = await fresh.PaymentTransactions.SingleAsync(item => item.ProviderOrderCode == orderCode);
        payment.Status.Should().Be("completed");
        payment.UserPlanId.Should().NotBeNull();
        var plans = await fresh.UserPlans.Where(item => item.UserId == scenario.UserId).ToListAsync();
        plans.Should().ContainSingle(item => item.Id == payment.UserPlanId && item.Status == "active");
        plans.Count(item => item.Status == "active").Should().Be(1);
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource!, options => options.UseVector());
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    private async Task BootstrapAuthAsync()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth; CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertAuthAsync(Guid id)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id)", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteAuthAsync(Guid id)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("DELETE FROM auth.users WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private const string WebhookBody = "{\"signature\":\"test\"}";
    private sealed record Scenario(Guid UserId, Plan Plan, long[] OrderCodes, Guid? PriorActivePlanId);
    private sealed record AttemptResult(object? Value, Exception? Exception);
    private sealed class DeterministicProvider(long orderCode, string liveStatus) : IPaymentProvider
    {
        public string ProviderName => "DeterministicPayOS";
        public string WebhookStatus { get; set; } = "PAID";
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TransactionStatusResult> GetTransactionStatusAsync(long requestedOrderCode, CancellationToken ct = default)
            => Task.FromResult(new TransactionStatusResult(true, requestedOrderCode, "test-link", liveStatus, liveStatus, 100, 100, 0));
        public Task<WebhookVerificationResult> VerifyWebhookAsync(string rawBody, string signature, CancellationToken ct = default)
            => Task.FromResult(new WebhookVerificationResult(true, orderCode, "test-link", WebhookStatus, WebhookStatus, 100, 100, 0, null));
    }

    private sealed class ActivationSaveFailureInterceptor : SaveChangesInterceptor
    {
        private int _hitCount;
        public int HitCount => Volatile.Read(ref _hitCount);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<UserPlan>().Any(entry => entry.State == EntityState.Added) == true
                && eventData.Context.ChangeTracker.Entries<PaymentTransaction>().Any(entry => entry.Entity.Status == "completed")
                && Interlocked.CompareExchange(ref _hitCount, 1, 0) == 0)
                throw new DbUpdateException("deterministic activation save failure");
            return ValueTask.FromResult(result);
        }
    }
}
