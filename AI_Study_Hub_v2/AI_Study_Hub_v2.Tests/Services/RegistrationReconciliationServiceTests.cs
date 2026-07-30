using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class RegistrationReconciliationServiceTests
{
    [Test]
    public async Task RunOnceAsync_UsesBoundedDeterministicBatch_AndContinuesAfterOneFailure()
    {
        await using var env = new Environment();
        var ids = Enumerable.Range(0, 27).Select(_ => Guid.NewGuid()).OrderBy(id => id).ToList();
        foreach (var id in ids) env.AddOperation(id, RegistrationOperation.IdentityConfirmed, DateTimeOffset.UtcNow.AddMinutes(-1));
        var calls = new List<Guid>();
        env.Coordinator.Setup(coordinator => coordinator.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => calls.Add(id))
            .Returns<Guid, CancellationToken>((id, _) => id == ids[0] ? Task.FromException(new InvalidOperationException("must not stop later work")) : Task.CompletedTask);

        await env.Service.RunOnceAsync();

        calls.Should().HaveCount(25);
        calls.Should().Equal(ids.Take(25));
    }

    [Test]
    public async Task RunOnceAsync_PurgesOnlyBoundedOldTerminalRows_WithoutTouchingUsers()
    {
        await using var env = new Environment();
        var old = DateTimeOffset.UtcNow.AddDays(-31);
        for (var index = 0; index < 26; index++) env.AddOperation(Guid.NewGuid(), index % 2 == 0 ? RegistrationOperation.Completed : RegistrationOperation.Conflict, old.AddMinutes(index));
        var profileCommitted = Guid.NewGuid(); env.AddOperation(profileCommitted, RegistrationOperation.ProfileCommitted, old.AddHours(1));
        var freshTerminal = Guid.NewGuid(); env.AddOperation(freshTerminal, RegistrationOperation.Compensated, DateTimeOffset.UtcNow.AddDays(-1));
        var active = Guid.NewGuid(); env.AddOperation(active, RegistrationOperation.IdentityConfirmed, old);
        env.AddUser();

        await env.Service.RunOnceAsync();

        env.OperationCount().Should().Be(4); // 27 old terminals - 25, plus fresh and active
        env.HasOperation(profileCommitted).Should().BeTrue();
        env.HasOperation(freshTerminal).Should().BeTrue();
        env.HasOperation(active).Should().BeTrue();
        env.UserCount().Should().Be(1);
    }

    [Test]
    public async Task RunOnceAsync_SelectsOnlyDueOrStaleRows_WithoutDeferredStarvation()
    {
        await using var env = new Environment();
        var now = DateTimeOffset.UtcNow;
        var expiredPrepared = env.AddOperation(RegistrationOperation.Prepared, now.AddHours(-25));
        var staleCreating = env.AddOperation(RegistrationOperation.CreatingIdentity, now.AddMinutes(-5), leaseExpiresAt: now.AddMinutes(-1));
        var staleFinalizing = env.AddOperation(RegistrationOperation.FinalizingProfile, now.AddMinutes(-4), leaseExpiresAt: now.AddMinutes(-1));
        var staleCompensating = env.AddOperation(RegistrationOperation.Compensating, now.AddMinutes(-3), leaseExpiresAt: now.AddMinutes(-1));
        var dueIdentity = env.AddOperation(RegistrationOperation.IdentityConfirmed, now.AddMinutes(-2));
        var dueCompensation = env.AddOperation(RegistrationOperation.CompensationRequired, now.AddMinutes(-1), nextAttemptAt: now.AddMinutes(-20));

        var deferredIdentity = env.AddOperation(RegistrationOperation.IdentityConfirmed, now.AddDays(-10), nextAttemptAt: now.AddMinutes(10));
        var deferredCompensation = env.AddOperation(RegistrationOperation.CompensationRequired, now.AddDays(-10), nextAttemptAt: now.AddMinutes(10));
        var freshPrepared = env.AddOperation(RegistrationOperation.Prepared, now.AddHours(-23));
        var activeCreating = env.AddOperation(RegistrationOperation.CreatingIdentity, now.AddDays(-10), leaseExpiresAt: now.AddMinutes(10));
        var activeFinalizing = env.AddOperation(RegistrationOperation.FinalizingProfile, now.AddDays(-10), leaseExpiresAt: now.AddMinutes(10));
        var activeCompensating = env.AddOperation(RegistrationOperation.Compensating, now.AddDays(-10), leaseExpiresAt: now.AddMinutes(10));
        var calls = new List<Guid>();
        env.Coordinator.Setup(coordinator => coordinator.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => calls.Add(id)).Returns(Task.CompletedTask);

        await env.Service.RunOnceAsync();

        calls.Should().Equal(expiredPrepared, dueCompensation, staleCreating, staleFinalizing, staleCompensating, dueIdentity);
        calls.Should().NotContain([deferredIdentity, deferredCompensation, freshPrepared, activeCreating, activeFinalizing, activeCompensating]);
    }

    [Test]
    public async Task HostedService_CancellationBeforeInitialDelay_DoesNotRunCycle()
    {
        var reconciliation = new Mock<IRegistrationReconciliationService>(MockBehavior.Strict);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(item => item.GetService(typeof(IRegistrationReconciliationService))).Returns(reconciliation.Object);
        var scope = new Mock<IServiceScope>(); scope.Setup(item => item.ServiceProvider).Returns(provider.Object);
        var scopes = new Mock<IServiceScopeFactory>(); scopes.Setup(item => item.CreateScope()).Returns(scope.Object);
        var hosted = new RegistrationReconciliationHostedService(scopes.Object, NullLogger<RegistrationReconciliationHostedService>.Instance);
        using var cancellation = new CancellationTokenSource();

        var started = hosted.StartAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await started;

        reconciliation.Verify(service => service.RunOnceAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HostedService_RunsOneScopedCycle_ThenStopsOnCancellation()
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconciliation = new Mock<IRegistrationReconciliationService>();
        reconciliation.Setup(service => service.RunOnceAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ran.TrySetResult()).Returns(Task.CompletedTask);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(item => item.GetService(typeof(IRegistrationReconciliationService))).Returns(reconciliation.Object);
        var scope = new Mock<IServiceScope>(); scope.Setup(item => item.ServiceProvider).Returns(provider.Object);
        var scopes = new Mock<IServiceScopeFactory>(); scopes.Setup(item => item.CreateScope()).Returns(scope.Object);
        var hosted = new RegistrationReconciliationHostedService(scopes.Object, NullLogger<RegistrationReconciliationHostedService>.Instance, TimeSpan.Zero, TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource();

        await hosted.StartAsync(cancellation.Token);
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await hosted.StopAsync(CancellationToken.None);

        reconciliation.Verify(service => service.RunOnceAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Environment : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public Mock<IRegistrationCoordinator> Coordinator { get; } = new();
        public RegistrationReconciliationService Service { get; }

        public Environment()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"registration-reconciliation-{Guid.NewGuid():N}").Options);
            services.AddScoped<AppDbContext, TestDbContext>();
            _provider = services.BuildServiceProvider(validateScopes: true);
            Service = new RegistrationReconciliationService(_provider.GetRequiredService<IServiceScopeFactory>(), Coordinator.Object, NullLogger<RegistrationReconciliationService>.Instance);
        }

        public void AddOperation(Guid id, string status, DateTimeOffset updatedAt) => AddOperation(status, updatedAt, id);

        public Guid AddOperation(string status, DateTimeOffset updatedAt, Guid? id = null, DateTimeOffset? nextAttemptAt = null, DateTimeOffset? leaseExpiresAt = null)
        {
            id ??= Guid.NewGuid();
            using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RegistrationOperations.Add(new RegistrationOperation { Id = id.Value, ProfileUserId = Guid.NewGuid(), NormalizedEmail = $"{id.Value:N}@example.test", Username = id.Value.ToString("N")[..12], FullName = "Test", Status = status, LeaseToken = leaseExpiresAt is null ? null : Guid.NewGuid(), LeaseExpiresAt = leaseExpiresAt, NextAttemptAt = nextAttemptAt, CreatedAt = updatedAt, UpdatedAt = updatedAt, CompletedAt = status is RegistrationOperation.Completed or RegistrationOperation.Compensated ? updatedAt : null });
            db.SaveChanges();
            return id.Value;
        }

        public void AddUser()
        {
            using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = Guid.NewGuid(), SupabaseUserId = Guid.NewGuid(), RoleId = 2, Username = "retained", FullName = "Retained", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); db.SaveChanges();
        }

        public int OperationCount() { using var scope = _provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>().RegistrationOperations.Count(); }
        public int UserCount() { using var scope = _provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Count(); }
        public bool HasOperation(Guid id) { using var scope = _provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>().RegistrationOperations.Any(item => item.Id == id); }
        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private sealed class TestDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentChunk>(); modelBuilder.Ignore<Document>(); modelBuilder.Ignore<Folder>(); }
    }
}
