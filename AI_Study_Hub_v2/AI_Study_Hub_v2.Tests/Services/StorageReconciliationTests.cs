using System.Text.Json;
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
public sealed class StorageReconciliationTests
{
    [Test]
    public async Task ReconcileAllAsync_PendingReservedOperation_ContributesReservedBytes()
    {
        await using var env = new ReconciliationEnvironment();
        var user = env.AddUser(cachedBytes: 0);
        env.AddOperation(user.Id, SharedFolderCopyOperation.Reserved, 37);

        var discrepancies = await env.Service.ReconcileAllAsync(default);

        discrepancies.Should().ContainSingle(item => item.UserId == user.Id && item.ActualBytes == 37 && item.Delta == 37);
        env.AssertStoredBytes(user.Id, 37);
    }

    [TestCase(SharedFolderCopyOperation.Copying)]
    [TestCase(SharedFolderCopyOperation.Compensating)]
    [TestCase(SharedFolderCopyOperation.CompensationRequired)]
    public async Task ReconcileAllAsync_ActiveReservationStatuses_ContributeReservedBytes(string status)
    {
        await using var env = new ReconciliationEnvironment();
        var user = env.AddUser(cachedBytes: 0);
        env.AddOperation(user.Id, status, 31);

        var discrepancies = await env.Service.ReconcileAllAsync(default);

        discrepancies.Should().ContainSingle(item => item.UserId == user.Id && item.ActualBytes == 31);
        env.AssertStoredBytes(user.Id, 31);
    }

    [Test]
    public async Task ReconcileAllAsync_CommittedDestinationWithStaleOperation_DoesNotDoubleCountReservation()
    {
        await using var env = new ReconciliationEnvironment();
        var user = env.AddUser(cachedBytes: 0);
        var destination = env.AddFolder(user.Id);
        env.AddDocument(user.Id, 19, destination.Id);
        env.AddOperation(user.Id, SharedFolderCopyOperation.CompensationRequired, 19, destination.Id);

        var discrepancies = await env.Service.ReconcileAllAsync(default);

        discrepancies.Should().ContainSingle(item => item.UserId == user.Id && item.ActualBytes == 19);
        env.AssertStoredBytes(user.Id, 19);
    }

    [Test]
    public async Task ReconcileAllAsync_DocumentAndPendingReservation_SumsExactly()
    {
        await using var env = new ReconciliationEnvironment();
        var user = env.AddUser(cachedBytes: 2);
        env.AddDocument(user.Id, 41);
        env.AddOperation(user.Id, SharedFolderCopyOperation.Reserved, 59);

        var discrepancies = await env.Service.ReconcileAllAsync(default);

        discrepancies.Should().ContainSingle(item => item.UserId == user.Id && item.CachedBytes == 2 && item.ActualBytes == 100 && item.Delta == 98);
        env.AssertStoredBytes(user.Id, 100);
    }

    [Test]
    public async Task ReconcileAllAsync_OtherUsersOperation_IsExcluded()
    {
        await using var env = new ReconciliationEnvironment();
        var destination = env.AddUser(cachedBytes: 0);
        var other = env.AddUser(cachedBytes: 0);
        env.AddOperation(other.Id, SharedFolderCopyOperation.Reserved, 73);

        var discrepancies = await env.Service.ReconcileAllAsync(default);

        discrepancies.Should().NotContain(item => item.UserId == destination.Id);
        discrepancies.Should().ContainSingle(item => item.UserId == other.Id && item.ActualBytes == 73);
        env.AssertStoredBytes(destination.Id, 0);
        env.AssertStoredBytes(other.Id, 73);
    }

    [Test]
    public async Task ReconcileUserAsync_OperationRemovalAndDocumentFinalization_PreservesExpectedTotal()
    {
        await using var env = new ReconciliationEnvironment();
        var user = env.AddUser(cachedBytes: 23);
        var operation = env.AddOperation(user.Id, SharedFolderCopyOperation.Copying, 23);

        await env.Service.ReconcileUserAsync(user.Id, default);
        env.AssertStoredBytes(user.Id, 23);
        env.InFresh(db =>
        {
            db.SharedFolderCopyOperations.Remove(db.SharedFolderCopyOperations.Single(item => item.Id == operation.Id));
            db.Documents.Add(NewDocument(user.Id, 23));
            db.SaveChanges();
        });

        await env.Service.ReconcileUserAsync(user.Id, default);

        env.AssertStoredBytes(user.Id, 23);
    }

    [Test]
    public async Task ReconcileAllAsync_NoOperationBehavior_RemainsCommittedDocumentTotal()
    {
        await using var env = new ReconciliationEnvironment();
        var user = env.AddUser(cachedBytes: 1);
        env.AddDocument(user.Id, 17);

        var discrepancies = await env.Service.ReconcileAllAsync(default);

        discrepancies.Should().ContainSingle(item => item.UserId == user.Id && item.ActualBytes == 17 && item.Delta == 16);
        env.AssertStoredBytes(user.Id, 17);
    }

    [Test]
    public async Task HostedService_ExecutesReconciliation_OnTrigger()
    {
        var reconciliationMock = new Mock<IStorageReconciliationService>();
        reconciliationMock.Setup(service => service.ReconcileAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(item => item.GetService(typeof(IStorageReconciliationService))).Returns(reconciliationMock.Object);
        var scope = new Mock<IServiceScope>(); scope.Setup(item => item.ServiceProvider).Returns(provider.Object);
        var scopes = new Mock<IServiceScopeFactory>(); scopes.Setup(item => item.CreateScope()).Returns(scope.Object);
        var hostedService = new StorageReconciliationHostedService(scopes.Object, NullLogger<StorageReconciliationHostedService>.Instance);
        using var cancellation = new CancellationTokenSource();

        var started = hostedService.StartAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await started;

        started.IsCompleted.Should().BeTrue();
    }

    private sealed class ReconciliationEnvironment : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public StorageReconciliationService Service { get; }

        public ReconciliationEnvironment()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"reconciliation-{Guid.NewGuid():N}").Options;
            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddScoped<AppDbContext, TestReconciliationDbContext>();
            _provider = services.BuildServiceProvider(validateScopes: true);
            Service = new StorageReconciliationService(_provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StorageReconciliationService>.Instance);
        }

        public User AddUser(long cachedBytes) { var user = new User { Id = Guid.NewGuid(), SupabaseUserId = Guid.NewGuid(), RoleId = 2, Username = Guid.NewGuid().ToString("N")[..12], StorageUsedBytes = cachedBytes, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; InFresh(db => { db.Users.Add(user); db.SaveChanges(); }); return user; }
        public Folder AddFolder(Guid userId) { var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "destination", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; InFresh(db => { db.Folders.Add(folder); db.SaveChanges(); }); return folder; }
        public void AddDocument(Guid userId, long bytes, Guid? folderId = null) => InFresh(db => { db.Documents.Add(NewDocument(userId, bytes, folderId)); db.SaveChanges(); });
        public SharedFolderCopyOperation AddOperation(Guid userId, string status, long bytes, Guid? destinationFolderId = null) { var operation = new SharedFolderCopyOperation { Id = Guid.NewGuid(), DestinationUserId = userId, SourceFolderId = Guid.NewGuid(), DestinationFolderId = destinationFolderId ?? Guid.NewGuid(), DestinationName = "copy", Status = status, ReservedStorageBytes = bytes, ManifestJson = JsonSerializer.Serialize(new { Version = 1, Items = Array.Empty<object>() }), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; InFresh(db => { db.SharedFolderCopyOperations.Add(operation); db.SaveChanges(); }); return operation; }
        public void AssertStoredBytes(Guid userId, long expected) => InFresh(db => db.Users.Single(item => item.Id == userId).StorageUsedBytes.Should().Be(expected));
        public void InFresh(Action<AppDbContext> action) { using var scope = _provider.CreateScope(); action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private static Document NewDocument(Guid userId, long bytes, Guid? folderId = null) => new() { Id = Guid.NewGuid(), UserId = userId, FolderId = folderId, FileName = "document.pdf", StoragePath = $"documents/{Guid.NewGuid():N}", FileSizeBytes = bytes, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

    private sealed class TestReconciliationDbContext : AppDbContext
    {
        public TestReconciliationDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentChunk>(); }
    }
}
