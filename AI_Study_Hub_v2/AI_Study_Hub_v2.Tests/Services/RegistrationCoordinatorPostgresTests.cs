using System.Collections.Concurrent;
using System.Data.Common;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector;

namespace AI_Study_Hub_v2.Tests.Services;

/// <summary>
/// Opt-in integration gate. It never contacts GoTrue: the provider is a deterministic in-process fake.
/// Set AI_STUDY_HUB_TEST_POSTGRES to a disposable database whose name ends in _test before selecting
/// Category=Postgres.
/// </summary>
[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class RegistrationCoordinatorPostgresTests
{
    private const string PreReSyncMigration = "20260706184528_AddDocumentEscalation";
    private const string ReSyncPlanMigration = "20260709165701_ReSyncPlanFkAndConstraints";
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _baseOptions;
    private readonly ConcurrentBag<Guid> _operationIds = [];
    private readonly ConcurrentBag<Guid> _profileIds = [];
    private readonly ConcurrentBag<Guid> _identityIds = [];
    private readonly ConcurrentBag<Guid> _planIds = [];

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Refusing registration PostgreSQL tests outside a database ending in _test.");

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();
        await BootstrapAuthAsync();
        _baseOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource, options => options.UseVector()).Options;
        await using var db = CreateDb();
        await MigrateCompatibilityAsync(db);
        await ApplyFolderDriftColumnsAsync(db);
    }

    [SetUp]
    public void SetUp()
    {
        _operationIds.Clear(); _profileIds.Clear(); _identityIds.Clear(); _planIds.Clear();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        if (_dataSource is null) return;
        await using var db = CreateDb();
        var operations = _operationIds.ToArray();
        var persisted = await db.RegistrationOperations.AsNoTracking()
            .Where(item => operations.Contains(item.Id)).ToListAsync();
        foreach (var operation in persisted)
        {
            _profileIds.Add(operation.ProfileUserId);
            if (operation.IdentityId is Guid identityId) _identityIds.Add(identityId);
        }
        var profiles = _profileIds.ToArray();
        db.UserPlans.RemoveRange(await db.UserPlans.Where(item => profiles.Contains(item.UserId)).ToListAsync());
        db.Users.RemoveRange(await db.Users.Where(item => profiles.Contains(item.Id)).ToListAsync());
        db.RegistrationOperations.RemoveRange(await db.RegistrationOperations.Where(item => operations.Contains(item.Id)).ToListAsync());
        db.Plans.RemoveRange(await db.Plans.Where(item => _planIds.Contains(item.Id)).ToListAsync());
        await db.SaveChangesAsync();
        foreach (var identity in _identityIds) await DeleteAuthAsync(identity);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
        _dataSource = null;
        _baseOptions = null;
    }

    [Test]
    public async Task Migration_EnforcesConstraintsUniqueProfileIdentityAndPartialIndexes()
    {
        (await ScalarAsync("SELECT to_regclass('public.registration_operations') IS NOT NULL")).Should().Be("True");
        var expectedConstraints = new[]
        {
            "ck_registration_operations_attempt_count_non_negative",
            "ck_registration_operations_id_non_empty",
            "ck_registration_operations_identity_id_non_empty",
            "ck_registration_operations_identity_required",
            "ck_registration_operations_lease_pair",
            "ck_registration_operations_lease_token_non_empty",
            "ck_registration_operations_profile_user_id_non_empty",
            "ck_registration_operations_status",
        };
        foreach (var name in expectedConstraints)
            (await ScalarAsync("SELECT contype::text || '|' || convalidated::text FROM pg_constraint WHERE conrelid='public.registration_operations'::regclass AND conname=@name", ("name", name))).Should().Be("c|true", name);

        var expectedIndexes = new Dictionary<string, string>
        {
            ["IX_registration_operations_profile_user_id"] = "CREATE UNIQUE INDEX \"IX_registration_operations_profile_user_id\" ON public.registration_operations USING btree (profile_user_id)",
            ["IX_registration_operations_identity_id"] = "CREATE UNIQUE INDEX \"IX_registration_operations_identity_id\" ON public.registration_operations USING btree (identity_id) WHERE (identity_id IS NOT NULL)",
            ["IX_registration_operations_normalized_email"] = "CREATE UNIQUE INDEX \"IX_registration_operations_normalized_email\" ON public.registration_operations USING btree (normalized_email) WHERE ((status)::text <> ALL ((ARRAY['Compensated'::character varying, 'Conflict'::character varying, 'Expired'::character varying])::text[]))",
            ["IX_registration_operations_username"] = "CREATE UNIQUE INDEX \"IX_registration_operations_username\" ON public.registration_operations USING btree (username) WHERE ((status)::text <> ALL ((ARRAY['Compensated'::character varying, 'Conflict'::character varying, 'Expired'::character varying])::text[]))",
            ["IX_registration_operations_lease_expires_at"] = "CREATE INDEX \"IX_registration_operations_lease_expires_at\" ON public.registration_operations USING btree (lease_expires_at)",
            ["IX_registration_operations_status_next_attempt_at_updated_at"] = "CREATE INDEX \"IX_registration_operations_status_next_attempt_at_updated_at\" ON public.registration_operations USING btree (status, next_attempt_at, updated_at)",
        };
        foreach (var (name, definition) in expectedIndexes)
            (await ScalarAsync("SELECT pg_get_indexdef(indexrelid) FROM pg_index WHERE indexrelid=@name::regclass", ("name", $"public.\"{name}\""))).Should().Be(definition, name);

        var invalid = new[]
        {
            "id='00000000-0000-0000-0000-000000000000'",
            "profile_user_id='00000000-0000-0000-0000-000000000000'",
            "identity_id='00000000-0000-0000-0000-000000000000'",
            "lease_token='00000000-0000-0000-0000-000000000000', lease_expires_at=now()",
            "lease_token=gen_random_uuid(), lease_expires_at=NULL",
            "attempt_count=-1",
            "status='NoSuchState'",
            "status='Completed', identity_id=NULL",
        };
        foreach (var mutation in invalid)
        {
            var exception = await FluentActions.Awaiting(() => InsertInvalidOperationAsync(mutation)).Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be("23514", mutation);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ConcurrentDifferentOperations_LeaveExactlyOneActiveEmailOrUsername(bool sameEmail)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var first = Request($"active-{suffix}@example.test", $"active{suffix[..8]}");
        var second = Request(sameEmail ? first.Email : $"other-{suffix}@example.test", sameEmail ? $"other{suffix[..8]}" : first.Username);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeGoTrue { OnCreate = async (email, _, _, app, _) =>
        {
            providerEntered.TrySetResult(true);
            await gate.Task;
            var operationId = Guid.Parse(app![GoTrueMetadata.RegistrationOperationIdKey]!.ToString()!);
            var request = operationId == first.RegistrationOperationId ? first : second;
            return new GoTrueUser { Id = Guid.NewGuid(), Email = email, UserMetadata = Marker(request.RegistrationOperationId), AppMetadata = Marker(request.RegistrationOperationId) };
        } };
        await using var host = CreateHost(provider);

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[] { TryPrepareAsync(host.Coordinator, first, start.Task), TryPrepareAsync(host.Coordinator, second, start.Task) };
        start.SetResult(true);
        await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        gate.TrySetResult(true);
        var results = await Task.WhenAll(attempts);
        results.Count(result => result).Should().Be(1);
        await using var db = CreateDb();
        var active = db.RegistrationOperations.Where(item => item.Status != RegistrationOperation.Compensated && item.Status != RegistrationOperation.Conflict && item.Status != RegistrationOperation.Expired);
        (sameEmail ? await active.CountAsync(item => item.NormalizedEmail == first.Email) : await active.CountAsync(item => item.Username == first.Username)).Should().Be(1);
    }

    [Test]
    public async Task ConcurrentSameOperation_CasAllowsExactlyOneProviderAdminCreate()
    {
        var request = Request();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeGoTrue { OnCreate = async (_, _, _, _, _) => { entered.TrySetResult(true); await release.Task; return Owned(request); } };
        await using var host = CreateHost(provider);
        var first = host.Coordinator.PrepareIdentityAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = TryPrepareAsync(host.Coordinator, request);
        release.TrySetResult(true);
        await first;
        await second;
        provider.CreateCalls.Should().Be(1);
    }

    [TestCase("absent")]
    [TestCase("retry")]
    [TestCase("conflict")]
    [TestCase("confirm")]
    public async Task StaleCreatingIdentityTakeover_OldOwnerCannotMutateAfterNewLease(string outcome)
    {
        var request = Request(); var oldLease = Guid.NewGuid(); var newLease = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.CreatingIdentity, oldLease, DateTimeOffset.UtcNow.AddMinutes(-2));
        var provider = new FakeGoTrue { Lookup = (_, _) =>
        {
            ReplaceLease(request.RegistrationOperationId, newLease);
            return outcome switch
            {
                "absent" => Task.FromResult<GoTrueUser?>(null),
                "retry" => throw new InvalidOperationException("simulated provider ambiguity"),
                "conflict" => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = request.Email }),
                _ => Task.FromResult<GoTrueUser?>(Owned(request)),
            };
        } };
        await using var host = CreateHost(provider);
        await host.Coordinator.ReconcileAsync(request.RegistrationOperationId);
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CreatingIdentity);
        operation.LeaseToken.Should().Be(newLease);
        operation.LastErrorCode.Should().BeNull();
    }

    [Test]
    public async Task StaleFinalizer_OldLeaseCannotOverwriteNewOwnerState()
    {
        var request = Request(); var oldLease = Guid.NewGuid(); var newLease = Guid.NewGuid(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.FinalizingProfile, oldLease, DateTimeOffset.UtcNow.AddMinutes(-2), identity);
        await AddExactProfileAsync(request.RegistrationOperationId);
        ReplaceFinalizerLease(request.RegistrationOperationId, newLease);
        await using var host = CreateHost(new FakeGoTrue());
        await InvokeAdvanceExactProfileAsync(host.Coordinator, request.RegistrationOperationId, identity, oldLease);
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.FinalizingProfile);
        operation.LeaseToken.Should().Be(newLease);
    }

    [Test]
    public async Task TwoCompensators_ExecuteOneDelete_AndOldLeaseCannotFinalize()
    {
        var request = Request(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.CompensationRequired, identityId: identity, error: "username_taken");
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeGoTrue { Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity)), OnDelete = async (_, _) => { entered.TrySetResult(true); await release.Task; } };
        await using var host = CreateHost(provider);
        var first = CatchAuthAsync(host.Coordinator.RegisterAsync(request));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = CatchAuthAsync(host.Coordinator.RegisterAsync(request));
        release.TrySetResult(true);
        await Task.WhenAll(first, second);
        provider.DeleteCalls.Should().Be(1);
        (await LoadAsync(request.RegistrationOperationId)).Status.Should().Be(RegistrationOperation.Compensated);
    }

    [Test]
    public async Task ProfileAndFreePlanAndProfileCommitted_AreAtomic()
    {
        var request = Request(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.IdentityConfirmed, identityId: identity);
        await EnsureFreePlanAsync();
        await using var host = CreateHost(new FakeGoTrue(), new ProfileCommitFaultInterceptor());
        await FluentActions.Awaiting(() => host.Coordinator.CompleteRegistrationAsync(request)).Should().ThrowAsync<AuthException>();
        await using var db = CreateDb();
        var operation = await LoadAsync(request.RegistrationOperationId);
        (await db.Users.AnyAsync(item => item.Id == operation.ProfileUserId)).Should().BeFalse();
        (await db.UserPlans.AnyAsync(item => item.UserId == operation.ProfileUserId)).Should().BeFalse();
        operation.Status.Should().Be(RegistrationOperation.IdentityConfirmed);
    }

    [Test]
    public async Task PostCommitAcknowledgementFailure_ResolvesExactProfileAndNeverDeletes()
    {
        var request = Request(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.IdentityConfirmed, identityId: identity);
        var provider = new FakeGoTrue { OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email)) };
        await using var host = CreateHost(provider, new ProfileCommitAcknowledgementFaultInterceptor());
        await host.Coordinator.CompleteRegistrationAsync(request);
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.Completed);
        operation.ProfileUserId.Should().NotBeEmpty(); operation.IdentityId.Should().Be(identity);
        await using var db = CreateDb();
        (await db.Users.CountAsync(item => item.Id == operation.ProfileUserId && item.SupabaseUserId == identity)).Should().Be(1);
        (await db.UserPlans.CountAsync(item => item.UserId == operation.ProfileUserId)).Should().BeLessOrEqualTo(1);
        provider.DeleteCalls.Should().Be(0);
    }

    [Test]
    public async Task RollbackWithNoProfile_ReturnsRecoverableIdentityState()
    {
        var request = Request();
        await SeedOperationAsync(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid());
        await using var host = CreateHost(new FakeGoTrue(), new ProfileCommitFaultInterceptor());
        await CatchAuthAsync(host.Coordinator.CompleteRegistrationAsync(request));
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.IdentityConfirmed);
        operation.LeaseToken.Should().BeNull();
        await using var db = CreateDb();
        (await db.Users.AnyAsync(item => item.Id == operation.ProfileUserId)).Should().BeFalse();
        (await db.UserPlans.AnyAsync(item => item.UserId == operation.ProfileUserId)).Should().BeFalse();
    }

    [Test]
    public async Task FinalizationVersusCompensation_ExactProfilePreventsProviderDeletion()
    {
        var request = Request(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.CompensationRequired, identityId: identity, error: "username_taken");
        await AddExactProfileAsync(request.RegistrationOperationId);
        var provider = new FakeGoTrue { OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email)) };
        await using var host = CreateHost(provider);
        await host.Coordinator.RegisterAsync(request);
        provider.DeleteCalls.Should().Be(0);
        (await LoadAsync(request.RegistrationOperationId)).Status.Should().Be(RegistrationOperation.Completed);
    }

    [Test]
    public async Task FinalizationVersusCompensation_ConditionalCompensationCannotPassLockedFinalizer()
    {
        var request = Request(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.IdentityConfirmed, identityId: identity);
        var barrier = new LockedProfileTransactionBarrierInterceptor();
        var provider = new FakeGoTrue { OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email)) };
        await using var finalizer = CreateHost(provider, barrier);
        await using var compensator = CreateHost(provider);
        var finalization = finalizer.Coordinator.CompleteRegistrationAsync(request);
        await barrier.ProfileRowLocked.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var finalizerLease = (await LoadAsync(request.RegistrationOperationId)).LeaseToken;
        finalizerLease.Should().NotBeNull();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await _dataSource!.OpenConnectionAsync(timeout.Token);
        await using var command = new NpgsqlCommand("""
            UPDATE registration_operations
            SET status = 'CompensationRequired', lease_token = NULL, lease_expires_at = NULL,
                last_error_code = 'profile_finalization_failed', updated_at = now()
            WHERE id = @id AND status = 'FinalizingProfile' AND lease_token = @lease
            """, connection);
        command.Parameters.AddWithValue("id", request.RegistrationOperationId);
        command.Parameters.AddWithValue("lease", finalizerLease!.Value);
        command.CommandTimeout = 10;
        var competingUpdate = command.ExecuteNonQueryAsync(timeout.Token);

        try
        {
            var completed = await Task.WhenAny(competingUpdate, Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token));
            completed.Should().NotBeSameAs(competingUpdate,
                "the conditional compensation update must wait for the finalizer's FOR UPDATE lock");
        }
        finally
        {
            barrier.Release();
        }

        await finalization.WaitAsync(timeout.Token);
        (await competingUpdate.WaitAsync(timeout.Token)).Should().Be(0);
        await compensator.Coordinator.RegisterAsync(request, timeout.Token);

        var operation = await LoadAsync(request.RegistrationOperationId);
        await using var db = CreateDb();
        (await db.Users.CountAsync(item => item.Id == operation.ProfileUserId && item.SupabaseUserId == identity)).Should().Be(1);
        operation.Status.Should().BeOneOf(RegistrationOperation.ProfileCommitted, RegistrationOperation.Completed);
        provider.DeleteCalls.Should().Be(0);
    }

    [Test]
    public async Task StaleCompensationOldOwnerPausedAfterLookup_NewOwnerWinsAndOldLeaseCannotMutate()
    {
        var request = Request(); var identity = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.CompensationRequired, identityId: identity, error: "username_taken");
        var lookupReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeGoTrue
        {
            Lookup = async (_, _) => { lookupReached.TrySetResult(true); await releaseLookup.Task; return Owned(request, identity); },
            OnDelete = (_, _) => Task.CompletedTask,
        };
        await using var oldHost = CreateHost(provider);
        var old = CatchAuthAsync(oldHost.Coordinator.RegisterAsync(request));
        await lookupReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var oldLease = (await LoadAsync(request.RegistrationOperationId)).LeaseToken!.Value;
        await ExecuteAsync("UPDATE registration_operations SET lease_expires_at=now()-interval '1 second' WHERE id=@id", ("id", request.RegistrationOperationId));
        var replacement = new FakeGoTrue { Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity)), OnDelete = (_, _) => Task.CompletedTask };
        await using var replacementHost = CreateHost(replacement);
        await CatchAuthAsync(replacementHost.Coordinator.RegisterAsync(request));
        releaseLookup.TrySetResult(true);
        await old;
        (provider.DeleteCalls + replacement.DeleteCalls).Should().Be(1);
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.LeaseToken.Should().NotBe(oldLease);
        operation.Status.Should().NotBe(RegistrationOperation.Compensating);
        await InvokeMutateCompensationAsync(oldHost.Coordinator, request.RegistrationOperationId, oldLease, RegistrationOperation.CompensationRequired);
        (await LoadAsync(request.RegistrationOperationId)).Status.Should().Be(operation.Status);
    }

    [Test]
    public async Task CompensationLeaseRenewal_BlocksContenderWithStaleExpirySnapshot()
    {
        var request = Request(); var identity = Guid.NewGuid(); var ownerLease = Guid.NewGuid();
        await SeedOperationAsync(request, RegistrationOperation.Compensating, ownerLease, DateTimeOffset.UtcNow.AddMinutes(-1), identity, "username_taken");
        var staleObservation = await LoadAsync(request.RegistrationOperationId);
        await using var host = CreateHost(new FakeGoTrue());

        (await InvokeRenewCompensationLeaseForDeleteAsync(host.Coordinator, request.RegistrationOperationId, ownerLease, identity)).Should().BeTrue();
        (await InvokeTryClaimCompensationAsync(host.Coordinator, staleObservation, Guid.NewGuid())).Should().BeFalse();

        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.Compensating);
        operation.LeaseToken.Should().Be(ownerLease);
        operation.LeaseExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task CompensationRequiredStaleObservation_CannotBypassPersistedFutureBackoff()
    {
        var request = Request();
        await SeedOperationAsync(request, RegistrationOperation.CompensationRequired, identityId: Guid.NewGuid(), error: "username_taken");
        var staleObservation = await LoadAsync(request.RegistrationOperationId);
        await ExecuteAsync("UPDATE registration_operations SET next_attempt_at=now()+interval '1 minute', updated_at=now() WHERE id=@id", ("id", request.RegistrationOperationId));
        await using var host = CreateHost(new FakeGoTrue());

        (await InvokeTryClaimCompensationAsync(host.Coordinator, staleObservation, Guid.NewGuid())).Should().BeFalse();

        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CompensationRequired);
        operation.LeaseToken.Should().BeNull();
        operation.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task CompletedRetainsEmailAndUsername()
    {
        var request = Request(); await SeedOperationAsync(request, RegistrationOperation.Completed, identityId: Guid.NewGuid());
        await FluentActions.Awaiting(() => SeedOperationAsync(Request(request.Email, request.Username), RegistrationOperation.Prepared)).Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task ProfileCommittedRetainsEmailAndUsername()
    {
        var request = Request(); await SeedOperationAsync(request, RegistrationOperation.ProfileCommitted, identityId: Guid.NewGuid());
        await FluentActions.Awaiting(() => SeedOperationAsync(Request(request.Email, request.Username), RegistrationOperation.Prepared)).Should().ThrowAsync<DbUpdateException>();
    }

    [TestCase(RegistrationOperation.Compensated)]
    [TestCase(RegistrationOperation.Conflict)]
    [TestCase(RegistrationOperation.Expired)]
    public async Task TerminalReleaseStatesReleaseEmailAndUsername(string status)
    {
        var request = Request(); await SeedOperationAsync(request, status);
        await FluentActions.Awaiting(() => SeedOperationAsync(Request(request.Email, request.Username), RegistrationOperation.Prepared)).Should().NotThrowAsync();
    }

    [Test]
    public async Task CancellationAfterExternalCreation_LeavesDurableRecoverableIdentityState()
    {
        var request = Request(); var identity = Guid.NewGuid();
        using var cancelled = new CancellationTokenSource();
        var provider = new FakeGoTrue
        {
            OnCreate = (_, _, _, _, _) => { cancelled.Cancel(); throw new OperationCanceledException(cancelled.Token); },
            Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity)),
        };
        await using var host = CreateHost(provider);
        await FluentActions.Awaiting(() => host.Coordinator.PrepareIdentityAsync(request, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.IdentityConfirmed);
        operation.IdentityId.Should().Be(identity);
    }

    [Test]
    public async Task CancellationDuringIdentityConfirmation_RethrowsOriginalAndLeavesRecoverableState()
    {
        var request = Request(); var identity = Guid.NewGuid(); using var cancelled = new CancellationTokenSource();
        var provider = new FakeGoTrue { OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request, identity)) };
        var interceptor = new CancelIdentityConfirmationInterceptor(cancelled);
        await using var host = CreateHost(provider, interceptor);
        var exception = await FluentActions.Awaiting(() => host.Coordinator.PrepareIdentityAsync(request, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancelled.Token);
        interceptor.HitCount.Should().Be(1);
        provider.CreateCalls.Should().Be(1);
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.IdentityConfirmed);
        operation.IdentityId.Should().Be(identity);
        operation.LeaseToken.Should().BeNull();
    }

    [Test]
    public async Task CancellationDuringProfileFinalization_RethrowsOriginalAndLeavesRecoverableState()
    {
        var request = Request(); var identity = Guid.NewGuid(); using var cancelled = new CancellationTokenSource();
        await SeedOperationAsync(request, RegistrationOperation.IdentityConfirmed, identityId: identity);
        await using var host = CreateHost(new FakeGoTrue(), new CancelProfileCommitInterceptor(cancelled));
        await FluentActions.Awaiting(() => host.Coordinator.CompleteRegistrationAsync(request, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        var operation = await LoadAsync(request.RegistrationOperationId);
        operation.Status.Should().BeOneOf(RegistrationOperation.IdentityConfirmed, RegistrationOperation.FinalizingProfile, RegistrationOperation.ProfileCommitted);
    }

    [Test]
    public async Task CancellationDuringCompensation_RethrowsOriginalAndLeavesDurableState()
    {
        var request = Request(); var identity = Guid.NewGuid(); using var cancelled = new CancellationTokenSource();
        await SeedOperationAsync(request, RegistrationOperation.CompensationRequired, identityId: identity, error: "username_taken");
        var provider = new FakeGoTrue { Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity)), OnDelete = (_, _) => { cancelled.Cancel(); throw new OperationCanceledException(cancelled.Token); } };
        await using var host = CreateHost(provider);
        await FluentActions.Awaiting(() => host.Coordinator.RegisterAsync(request, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        (await LoadAsync(request.RegistrationOperationId)).Status.Should().BeOneOf(RegistrationOperation.Compensating, RegistrationOperation.CompensationRequired, RegistrationOperation.Compensated);
    }

    private async Task SeedOperationAsync(RegisterRequest request, string status, Guid? lease = null, DateTimeOffset? expires = null, Guid? identityId = null, string? error = null)
    {
        var profile = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        _operationIds.Add(request.RegistrationOperationId); _profileIds.Add(profile);
        if (identityId is Guid identity)
        {
            _identityIds.Add(identity);
            await InsertAuthAsync(identity);
        }
        await using var db = CreateDb();
        db.RegistrationOperations.Add(new RegistrationOperation { Id = request.RegistrationOperationId, NormalizedEmail = request.Email.Trim().ToLowerInvariant(), Username = request.Username, FullName = request.FullName, ProfileUserId = profile, Status = status, IdentityId = identityId, LeaseToken = lease, LeaseExpiresAt = expires, LastErrorCode = error, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }

    private async Task AddExactProfileAsync(Guid operationId)
    {
        await using var db = CreateDb(); var operation = await db.RegistrationOperations.SingleAsync(item => item.Id == operationId);
        db.Users.Add(new User { Id = operation.ProfileUserId, SupabaseUserId = operation.IdentityId!.Value, RoleId = await StudentRoleIdAsync(db), Username = operation.Username, FullName = operation.FullName, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task EnsureFreePlanAsync()
    {
        await using var db = CreateDb();
        if (await db.Plans.AnyAsync(item => item.PlanKey == "free" && item.IsActive)) return;
        var plan = new Plan { Id = Guid.NewGuid(), PlanKey = "free", DisplayName = "Free", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        _planIds.Add(plan.Id); db.Plans.Add(plan); await db.SaveChangesAsync();
    }

    private void ReplaceLease(Guid operationId, Guid lease) => ExecuteAsync("UPDATE registration_operations SET lease_token=@lease, lease_expires_at=now() + interval '2 minutes', last_error_code=NULL WHERE id=@id", ("lease", lease), ("id", operationId)).GetAwaiter().GetResult();
    private void ReplaceFinalizerLease(Guid operationId, Guid lease) => ReplaceLease(operationId, lease);
    private async Task<RegistrationOperation> LoadAsync(Guid id) { await using var db = CreateDb(); return await db.RegistrationOperations.AsNoTracking().SingleAsync(item => item.Id == id); }
    private RegisterRequest Request(string? email = null, string? username = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var request = new RegisterRequest { RegistrationOperationId = Guid.NewGuid(), Email = email ?? $"alice-{suffix}@example.test", Username = username ?? $"alice{suffix[..10]}", FullName = "Alice", Password = "Password!1" };
        _operationIds.Add(request.RegistrationOperationId);
        return request;
    }
    private static GoTrueUser Owned(RegisterRequest request, Guid? identity = null) => new() { Id = identity ?? Guid.NewGuid(), Email = request.Email.Trim().ToLowerInvariant(), UserMetadata = Marker(request.RegistrationOperationId), AppMetadata = Marker(request.RegistrationOperationId) };
    private static GoTrueSession Session(Guid identity, string email) => new() { AccessToken = "test", RefreshToken = "test", ExpiresIn = 900, User = new GoTrueUser { Id = identity, Email = email } };
    private static Dictionary<string, object?> Marker(Guid operationId) => new() { [GoTrueMetadata.RegistrationOperationIdKey] = operationId.ToString() };
    private static async Task<bool> TryPrepareAsync(RegistrationCoordinator coordinator, RegisterRequest request, Task? start = null) { try { if (start is not null) await start; await coordinator.PrepareIdentityAsync(request); return true; } catch (AuthException) { return false; } }
    private static async Task<AuthException> CatchAuthAsync(Task task) { try { await task; } catch (AuthException error) { return error; } throw new AssertionException("Expected AuthException."); }
    private static Task<RegistrationOperation?> InvokeAdvanceExactProfileAsync(RegistrationCoordinator coordinator, Guid operationId, Guid identityId, Guid lease) => (Task<RegistrationOperation?>)typeof(RegistrationCoordinator).GetMethod("AdvanceExactProfileAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(coordinator, [operationId, identityId, lease, CancellationToken.None])!;
    private static Task<RegistrationOperation> InvokeMutateCompensationAsync(RegistrationCoordinator coordinator, Guid operationId, Guid lease, string status) => (Task<RegistrationOperation>)typeof(RegistrationCoordinator).GetMethod("MutateCompensationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(coordinator, [operationId, lease, status, null, CancellationToken.None])!;
    private static Task<bool> InvokeTryClaimCompensationAsync(RegistrationCoordinator coordinator, RegistrationOperation observed, Guid lease) => (Task<bool>)typeof(RegistrationCoordinator).GetMethod("TryClaimCompensationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(coordinator, [observed, lease, CancellationToken.None])!;
    private static Task<bool> InvokeRenewCompensationLeaseForDeleteAsync(RegistrationCoordinator coordinator, Guid operationId, Guid lease, Guid identityId) => (Task<bool>)typeof(RegistrationCoordinator).GetMethod("RenewCompensationLeaseForDeleteAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(coordinator, [operationId, lease, identityId, CancellationToken.None])!;

    private Host CreateHost(FakeGoTrue provider, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection(); services.AddLogging();
        var options = interceptors.Length == 0
            ? _baseOptions!
            : new DbContextOptionsBuilder<AppDbContext>(_baseOptions!).AddInterceptors(interceptors).Options;
        services.AddTransient<AppDbContext>(_ => new AppDbContext(options));
        services.AddScoped<ISelfRegistrationPolicy, AllowPolicy>();
        services.AddScoped<RegistrationCoordinator>();
        var serviceProvider = services.BuildServiceProvider(validateScopes: true); var scope = serviceProvider.CreateScope();
        return new Host(serviceProvider, scope, new RegistrationCoordinator(scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), provider, NullLogger<RegistrationCoordinator>.Instance));
    }

    private AppDbContext CreateDb() => new(_baseOptions!);
    private async Task<int> StudentRoleIdAsync(AppDbContext db) => (await db.Roles.SingleAsync(item => item.RoleName == Role.StudentRoleName)).Id;
    private async Task BootstrapAuthAsync() { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth; CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);", connection); await command.ExecuteNonQueryAsync(); }
    private async Task DeleteAuthAsync(Guid id) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand("DELETE FROM auth.users WHERE id=@id", connection); command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync(); }
    private async Task InsertAuthAsync(Guid id) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id) ON CONFLICT (id) DO NOTHING", connection); command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync(); }
    private async Task<string> ScalarAsync(string sql, params (string Name, object Value)[] values) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand(sql, connection); foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); return (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty; }
    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] values) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand(sql, connection); foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); await command.ExecuteNonQueryAsync(); }
    private async Task InsertInvalidOperationAsync(string mutation)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var insert = new NpgsqlCommand("INSERT INTO registration_operations (id, normalized_email, username, full_name, profile_user_id, status, attempt_count, created_at, updated_at) VALUES (gen_random_uuid(), 'invalid@example.test', 'invalid', 'Invalid', gen_random_uuid(), 'Prepared', 0, now(), now()) RETURNING id", connection, transaction))
        {
            var id = (Guid)(await insert.ExecuteScalarAsync())!;
            await using var mutate = new NpgsqlCommand($"UPDATE registration_operations SET {mutation} WHERE id=@id", connection, transaction);
            mutate.Parameters.AddWithValue("id", id);
            await mutate.ExecuteNonQueryAsync();
        }
        await transaction.RollbackAsync();
    }
    private static async Task MigrateCompatibilityAsync(AppDbContext db) { var applied = await db.Database.GetAppliedMigrationsAsync(); if (!applied.Contains(ReSyncPlanMigration)) { if (!applied.Contains(PreReSyncMigration)) await db.Database.GetService<IMigrator>().MigrateAsync(PreReSyncMigration); await db.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\""); } await db.Database.MigrateAsync(); }
    private static Task ApplyFolderDriftColumnsAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("ALTER TABLE public.folders ADD COLUMN IF NOT EXISTS share_review_source varchar(32), ADD COLUMN IF NOT EXISTS ai_review_reason varchar(2000), ADD COLUMN IF NOT EXISTS ai_review_confidence double precision, ADD COLUMN IF NOT EXISTS ai_review_failure_count integer NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS human_review_reason varchar(2000), ADD COLUMN IF NOT EXISTS requires_human_review boolean NOT NULL DEFAULT false, ADD COLUMN IF NOT EXISTS appeal_requested_at timestamp with time zone, ADD COLUMN IF NOT EXISTS appeal_message varchar(2000)");

    private sealed class Host(ServiceProvider provider, IServiceScope scope, RegistrationCoordinator coordinator) : IAsyncDisposable { public RegistrationCoordinator Coordinator { get; } = coordinator; public async ValueTask DisposeAsync() { scope.Dispose(); await provider.DisposeAsync(); } }
    private sealed class AllowPolicy : ISelfRegistrationPolicy { public Task EnsureAllowedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class ProfileCommitFaultInterceptor : SaveChangesInterceptor { public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.ProfileCommitted) == true) throw new DbUpdateException("profile commit fault"); return ValueTask.FromResult(result); } }
    private sealed class ProfileCommitAcknowledgementFaultInterceptor : DbTransactionInterceptor { public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) => Task.FromException(new DbUpdateException("post-commit acknowledgement fault")); }
    private sealed class CancelIdentityConfirmationInterceptor(CancellationTokenSource source) : DbCommandInterceptor
    {
        private int _hitCount;
        public int HitCount => Volatile.Read(ref _hitCount);

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("registration_operations", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("SET", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("identity_id", StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(ref _hitCount, 1, 0) == 0)
            {
                source.Cancel();
                throw new OperationCanceledException(source.Token);
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CancelProfileCommitInterceptor(CancellationTokenSource source) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.ProfileCommitted) == true)
            { source.Cancel(); throw new OperationCanceledException(source.Token); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class LockedProfileTransactionBarrierInterceptor : DbCommandInterceptor
    {
        private int _paused;
        public TaskCompletionSource<bool> ProfileRowLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> ReleaseProfileTransaction { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("registration_operations", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            { ProfileRowLocked.TrySetResult(true); await ReleaseProfileTransaction.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            return result;
        }
        public void Release() => ReleaseProfileTransaction.TrySetResult(true);
    }
    private sealed class FakeGoTrue : IGoTrueClient
    {
        public int CreateCalls; public int DeleteCalls;
        public Func<string, string, Dictionary<string, object?>?, Dictionary<string, object?>?, CancellationToken, Task<GoTrueUser>> OnCreate { get; set; } = (_, _, _, _, _) => Task.FromResult(new GoTrueUser());
        public Func<string, CancellationToken, Task<GoTrueUser?>> Lookup { get; set; } = (_, _) => Task.FromResult<GoTrueUser?>(null);
        public Func<Guid, CancellationToken, Task> OnDelete { get; set; } = (_, _) => Task.CompletedTask;
        public Func<string, string, CancellationToken, Task<GoTrueSession>> OnSignIn { get; set; } = (_, _, _) => throw new InvalidOperationException();
        public Task<GoTrueUser> AdminCreateUserAsync(string email, string password, Dictionary<string, object?>? userMetadata, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default) { Interlocked.Increment(ref CreateCalls); return OnCreate(email, password, userMetadata, appMetadata, cancellationToken); }
        public Task<GoTrueUser?> AdminGetUserByEmailAsync(string email, CancellationToken cancellationToken = default) => Lookup(email, cancellationToken);
        public Task<GoTrueSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => OnSignIn(email, password, cancellationToken);
        public Task AdminDeleteUserAsync(Guid userId, CancellationToken cancellationToken = default) { Interlocked.Increment(ref DeleteCalls); return OnDelete(userId, cancellationToken); }
        public Task<GoTrueSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task SignOutAsync(string accessToken, bool global, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task<GoTrueUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task<GoTrueUser> UpdateUserAsync(string accessToken, string? email, string? password, Dictionary<string, object?>? metadata, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task<GoTrueUser> AdminUpdateUserByIdAsync(Guid userId, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task AdminSignOutUserAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
