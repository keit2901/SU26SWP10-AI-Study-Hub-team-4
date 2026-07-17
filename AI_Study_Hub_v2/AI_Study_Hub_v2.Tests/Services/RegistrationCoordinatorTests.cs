using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class RegistrationCoordinatorTests
{
    [Test]
    public async Task EmptyOperationId_RejectsBeforeDatabaseOrProvider()
    {
        await using var env = new Environment();
        var request = Request(); request.RegistrationOperationId = Guid.Empty;
        var act = () => env.Sut.PrepareIdentityAsync(request);
        var error = await act.Should().ThrowAsync<AuthException>();
        error.Which.Code.Should().Be("registration_operation_required");
        env.GoTrue.CreateCalls.Should().Be(0); env.Policy.Calls.Should().Be(0);
    }

    [Test]
    public async Task NewOperation_IsDurableBeforeAdminCreate_AndSendsBothMarkers()
    {
        await using var env = new Environment();
        var request = Request();
        env.GoTrue.OnCreate = (email, password, user, app, _) =>
        {
            env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.CreatingIdentity);
            user!["username"].Should().Be("alice"); user["full_name"].Should().Be("Alice");
            user[GoTrueMetadata.RegistrationOperationIdKey].Should().Be(request.RegistrationOperationId.ToString());
            app![GoTrueMetadata.RegistrationOperationIdKey].Should().Be(request.RegistrationOperationId.ToString());
            return Task.FromResult(Owned(request));
        };
        var operation = await env.Sut.PrepareIdentityAsync(request);
        operation.Status.Should().Be(RegistrationOperation.IdentityConfirmed); operation.IdentityId.Should().NotBeEmpty();
        operation.ProfileUserId.Should().NotBeEmpty(); env.Policy.Calls.Should().Be(1);
    }

    [Test]
    public async Task ExactRetry_ResumesWithoutPolicy_Recheck_AndDifferentPayloadConflicts()
    {
        await using var env = new Environment();
        var request = Request(); env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request));
        await env.Sut.PrepareIdentityAsync(request);
        await env.Sut.PrepareIdentityAsync(request);
        env.Policy.Calls.Should().Be(1); env.GoTrue.CreateCalls.Should().Be(1);
        request.FullName = "Other";
        var act = () => env.Sut.PrepareIdentityAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_operation_conflict");
    }

    [Test]
    public async Task NewOperation_EnforcesLocalAndActiveConflicts_AndPolicyOnlyOnce()
    {
        await using var env = new Environment();
        env.AddUser("taken");
        var local = Request(username: "taken");
        var localAct = () => env.Sut.PrepareIdentityAsync(local);
        (await localAct.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("username_taken");
        env.AddOperation(Request(email: "progress@example.test"), RegistrationOperation.Prepared);
        var activeAct = () => env.Sut.PrepareIdentityAsync(Request(email: "progress@example.test", username: "other"));
        (await activeAct.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_in_progress");
        env.AddOperation(Request(email: "other@example.test", username: "progress"), RegistrationOperation.Prepared);
        var usernameAct = () => env.Sut.PrepareIdentityAsync(Request(username: "progress"));
        (await usernameAct.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("username_registration_in_progress");
        env.Policy.Calls.Should().Be(3);
    }

    [Test]
    public async Task UnexpiredLease_Blocks_WhileStaleLease_RecoversOwnedIdentity()
    {
        await using var env = new Environment();
        var request = Request(); env.AddOperation(request, RegistrationOperation.CreatingIdentity, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));
        var pending = () => env.Sut.PrepareIdentityAsync(request);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var stale = Request(); env.AddOperation(stale, RegistrationOperation.CreatingIdentity, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(stale));
        (await env.Sut.PrepareIdentityAsync(stale)).Status.Should().Be(RegistrationOperation.IdentityConfirmed);
    }

    [Test]
    public async Task CompetingClaims_AllowExactlyOneAdminCreate()
    {
        await using var env = new Environment();
        var request = Request();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        env.GoTrue.OnCreate = async (_, _, _, _, _) => { entered.SetResult(); await release.Task; return Owned(request); };
        var first = env.Sut.PrepareIdentityAsync(request);
        await entered.Task;
        var second = () => env.Sut.PrepareIdentityAsync(request);
        (await second.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        release.SetResult();
        (await first).Status.Should().Be(RegistrationOperation.IdentityConfirmed);
        env.GoTrue.CreateCalls.Should().Be(1);
    }

    [TestCase("user")]
    [TestCase("app")]
    [TestCase("wrong")]
    public async Task MissingOrMismatchedOwnershipMarker_Conflicts(string kind)
    {
        await using var env = new Environment();
        var request = Request();
        env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request, kind));
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = request.Email });
        var act = () => env.Sut.PrepareIdentityAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("email_already_registered");
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Conflict);
    }

    [Test]
    public async Task PartialCreateSuccess_UsesAuthoritativeLookup_ForExactAbsentAndUnrelated()
    {
        await using var env = new Environment();
        var exact = Request();
        env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(new GoTrueUser { Id = Guid.Empty, Email = exact.Email });
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(exact));
        (await env.Sut.PrepareIdentityAsync(exact)).Status.Should().Be(RegistrationOperation.IdentityConfirmed);

        var absent = Request("partial-absent@example.test", "partialabsent");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(null);
        var pending = () => env.Sut.PrepareIdentityAsync(absent);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        env.Load(absent.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Prepared);

        var unrelated = Request("partial-unrelated@example.test", "partialunrelated");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = unrelated.Email });
        var conflict = () => env.Sut.PrepareIdentityAsync(unrelated);
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("email_already_registered");
    }

    [Test]
    public async Task AmbiguousCreate_UsesLookupToConfirm_OrReturnsPreparedWhenAbsent()
    {
        await using var env = new Environment();
        var request = Request();
        env.GoTrue.OnCreate = (_, _, _, _, _) => throw new InvalidOperationException("transport");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request));
        (await env.Sut.PrepareIdentityAsync(request)).Status.Should().Be(RegistrationOperation.IdentityConfirmed);
        var absent = Request("absent@example.test", "absent"); env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(null);
        var pending = () => env.Sut.PrepareIdentityAsync(absent);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var result = env.Load(absent.RegistrationOperationId);
        result.Status.Should().Be(RegistrationOperation.Prepared); result.LeaseToken.Should().BeNull(); result.LastErrorCode.Should().Be("identity_create_ambiguous");
    }

    [Test]
    public async Task LookupUnrelated_Conflicts_AndLookupFailureRemainsRetryable()
    {
        await using var env = new Environment();
        var request = Request(); env.GoTrue.OnCreate = (_, _, _, _, _) => throw new InvalidOperationException();
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = request.Email });
        var conflict = () => env.Sut.PrepareIdentityAsync(request);
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("email_already_registered");
        var retry = Request(); env.GoTrue.Lookup = (_, _) => throw new InvalidOperationException("lookup");
        var pending = () => env.Sut.PrepareIdentityAsync(retry);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var operation = env.Load(retry.RegistrationOperationId); operation.Status.Should().Be(RegistrationOperation.CreatingIdentity); operation.LeaseToken.Should().NotBeNull(); operation.NextAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task Cancellation_UsesIndependentRecoveryToken_ThenPropagates()
    {
        await using var env = new Environment();
        var request = Request(); using var cancelled = new CancellationTokenSource();
        env.GoTrue.OnCreate = (_, _, _, _, _) => throw new OperationCanceledException(cancelled.Token);
        var independentLookup = false;
        env.GoTrue.Lookup = (_, token) => { independentLookup = token != cancelled.Token && !token.IsCancellationRequested; return Task.FromResult<GoTrueUser?>(null); };
        var act = () => env.Sut.PrepareIdentityAsync(request, cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        independentLookup.Should().BeTrue();
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Prepared);
    }

    [TestCase("timeout")]
    [TestCase("auth")]
    [TestCase("generic")]
    [TestCase("unrelated")]
    public async Task Cancellation_AlwaysRethrowsOriginal_WhenRecoveryFailsOrConflicts(string recovery)
    {
        await using var env = new Environment();
        var request = Request(); using var cancellation = new CancellationTokenSource();
        env.GoTrue.OnCreate = (_, _, _, _, _) => throw new OperationCanceledException(cancellation.Token);
        env.GoTrue.Lookup = (_, token) => recovery switch
        {
            "timeout" => throw new OperationCanceledException(token),
            "auth" => throw new AuthException(503, "registration_identity_lookup_failed", "sanitized"),
            "generic" => throw new InvalidOperationException("provider body"),
            _ => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = request.Email }),
        };
        var act = () => env.Sut.PrepareIdentityAsync(request, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        var operation = env.Load(request.RegistrationOperationId);
        if (recovery == "unrelated") operation.Status.Should().Be(RegistrationOperation.Conflict);
        else { operation.Status.Should().Be(RegistrationOperation.CreatingIdentity); operation.NextAttemptAt.Should().NotBeNull(); }
    }

    [Test]
    public async Task ConflictRetry_IsStable_AndLookupAuthFailureIsRetryable()
    {
        await using var env = new Environment();
        var conflictRequest = Request(); env.AddOperation(conflictRequest, RegistrationOperation.Conflict);
        var conflict = () => env.Sut.PrepareIdentityAsync(conflictRequest);
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_conflict");

        var request = Request("lookup-auth@example.test", "lookupauth");
        env.GoTrue.OnCreate = (_, _, _, _, _) => throw new InvalidOperationException();
        env.GoTrue.Lookup = (_, _) => throw new AuthException(503, "registration_identity_lookup_failed", "sanitized");
        var pending = () => env.Sut.PrepareIdentityAsync(request);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CreatingIdentity); operation.LastErrorCode.Should().Be("identity_lookup_pending"); operation.NextAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task ConfirmationWriteFailure_ResolvesToDurablePending()
    {
        await using var env = new Environment(new ConfirmationWriteFailureInterceptor());
        var request = Request(); env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request));
        var pending = () => env.Sut.PrepareIdentityAsync(request);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CreatingIdentity);
        operation.LastErrorCode.Should().Be("identity_confirmation_pending"); operation.NextAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task ConfirmationCancellation_BeforeCommit_PreservesOriginalCancellationAndMakesRetryable()
    {
        await using var env = new Environment(new ConfirmationCancellationInterceptor());
        var request = Request(); env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request));
        var act = () => env.Sut.PrepareIdentityAsync(request);
        await act.Should().ThrowAsync<OperationCanceledException>();
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CreatingIdentity);
        operation.LastErrorCode.Should().Be("identity_confirmation_pending");
    }

    [Test]
    public async Task ConfirmationCancellation_AfterDurableCommit_StillRethrowsOriginalCancellation()
    {
        await using var env = new Environment(new ConfirmationPostCommitCancellationInterceptor());
        var request = Request(); env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request));
        var act = () => env.Sut.PrepareIdentityAsync(request);
        await act.Should().ThrowAsync<OperationCanceledException>();
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.IdentityConfirmed);
    }

    [Test]
    public async Task ConfirmationCancellation_AnotherLeaseRemainsUnchanged_AndOriginalCancellationWins()
    {
        await using var env = new Environment();
        var request = Request(); var callerLease = Guid.NewGuid(); var otherLease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CreatingIdentity, otherLease, DateTimeOffset.UtcNow.AddMinutes(1));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var act = () => ConfirmAsync(env.Sut, request.RegistrationOperationId, callerLease, Guid.NewGuid(), cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        var operation = env.Load(request.RegistrationOperationId);
        operation.LeaseToken.Should().Be(otherLease); operation.Status.Should().Be(RegistrationOperation.CreatingIdentity);
    }

    [Test]
    public async Task ConfirmationCancellation_WhenResolutionMutationFails_StillRethrowsOriginalCancellation()
    {
        await using var env = new Environment(new ConfirmationResolutionFailureInterceptor());
        var request = Request(); env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request));
        var act = () => env.Sut.PrepareIdentityAsync(request);
        await act.Should().ThrowAsync<OperationCanceledException>();
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.CreatingIdentity);
    }

    [TestCase(RegistrationOperation.IdentityConfirmed)]
    [TestCase(RegistrationOperation.FinalizingProfile)]
    [TestCase(RegistrationOperation.ProfileCommitted)]
    [TestCase(RegistrationOperation.Completed)]
    public async Task ConfirmationAmbiguity_AcceptsSameIdentityForwardState(string status)
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, status, identityId: identity);
        var result = await ResolveConfirmationAmbiguityAsync(env.Sut, request.RegistrationOperationId, Guid.NewGuid(), identity);
        result!.Status.Should().Be(status); result.IdentityId.Should().Be(identity);
    }

    [Test]
    public async Task ConfirmationAmbiguity_NeverMutatesOtherLeaseOrDifferentIdentity()
    {
        await using var env = new Environment();
        var request = Request(); var callerLease = Guid.NewGuid(); var otherLease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CreatingIdentity, otherLease, DateTimeOffset.UtcNow.AddMinutes(1));
        var pending = () => ResolveConfirmationAmbiguityAsync(env.Sut, request.RegistrationOperationId, callerLease, Guid.NewGuid());
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var unchanged = env.Load(request.RegistrationOperationId); unchanged.LeaseToken.Should().Be(otherLease); unchanged.Status.Should().Be(RegistrationOperation.CreatingIdentity);

        var different = Request("different@example.test", "different"); var durableIdentity = Guid.NewGuid();
        env.AddOperation(different, RegistrationOperation.IdentityConfirmed, identityId: durableIdentity);
        var conflict = () => ResolveConfirmationAmbiguityAsync(env.Sut, different.RegistrationOperationId, callerLease, Guid.NewGuid());
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_identity_conflict");
        env.Load(different.RegistrationOperationId).IdentityId.Should().Be(durableIdentity);
    }

    [Test]
    public async Task ConfirmationAmbiguity_PreparedMissingAndTerminal_AreStableWithoutMutation()
    {
        await using var env = new Environment();
        var request = Request(); var lease = Guid.NewGuid(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.Prepared);
        var prepared = () => ResolveConfirmationAmbiguityAsync(env.Sut, request.RegistrationOperationId, lease, identity);
        (await prepared.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Prepared);
        var missing = () => ResolveConfirmationAmbiguityAsync(env.Sut, Guid.NewGuid(), lease, identity);
        (await missing.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        var terminal = Request("terminal@example.test", "terminal"); env.AddOperation(terminal, RegistrationOperation.Conflict);
        var conflict = () => ResolveConfirmationAmbiguityAsync(env.Sut, terminal.RegistrationOperationId, lease, identity);
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_conflict");
    }

    [Test]
    public async Task ClaimWriteFailure_ResolvesPreparedToPendingWithoutProviderDispatch()
    {
        await using var env = new Environment(new ClaimWriteFailureInterceptor());
        var request = Request();
        var pending = () => env.Sut.PrepareIdentityAsync(request);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        env.GoTrue.CreateCalls.Should().Be(0);
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Prepared);
    }

    [Test]
    public void OperationModel_DoesNotPersistSecretsOrProviderResponses()
    {
        typeof(RegistrationOperation).GetProperties().Select(property => property.Name.ToLowerInvariant())
            .Should().NotContain(name => name.Contains("password") || name.Contains("token") && name != "leasetoken" || name.Contains("response"));
    }

    [Test]
    public void ActiveOperationQuery_IsTranslatedByNpgsql_WithoutDatabaseConnection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=postgres;Password=unused")
            .Options;
        using var db = new TranslationDbContext(options);
        var sql = RegistrationCoordinator.ActiveOperations(db.RegistrationOperations)
            .Where(item => item.NormalizedEmail == "alice@example.test").ToQueryString();
        sql.Should().Contain("Compensated").And.Contain("Conflict").And.Contain("Expired");
    }

    [Test]
    public async Task Completion_CommitsDeterministicProfileAndFreePlanBeforeSignIn()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); env.AddFreePlan();
        env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request, identity));
        env.GoTrue.OnSignIn = (_, _, _) =>
        {
            var operation = env.Load(request.RegistrationOperationId);
            operation.Status.Should().Be(RegistrationOperation.ProfileCommitted);
            env.UserCount().Should().Be(1); env.UserPlanCount().Should().Be(1);
            return Task.FromResult(Session(identity, request.Email));
        };
        var response = await env.Sut.CompleteRegistrationAsync(request);
        response.User.Id.Should().Be(env.Load(request.RegistrationOperationId).ProfileUserId);
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Completed);
    }

    [Test]
    public async Task Completion_MissingFreePlanStillSucceeds_AndSessionMismatchLeavesProfileCommitted()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.GoTrue.OnCreate = (_, _, _, _, _) => Task.FromResult(Owned(request, identity));
        env.GoTrue.OnSignIn = (_, _, _) => Task.FromResult(Session(Guid.NewGuid(), request.Email));
        var unavailable = () => env.Sut.CompleteRegistrationAsync(request);
        (await unavailable.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_session_unavailable");
        env.UserCount().Should().Be(1); env.UserPlanCount().Should().Be(0);
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.ProfileCommitted);
    }

    [Test]
    public async Task Completion_ExistingExactProfileForwardRecoversWithoutDuplication_AndCompletedRetryOnlySignsIn()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: identity);
        var operation = env.Load(request.RegistrationOperationId);
        env.AddExactProfile(operation);
        env.GoTrue.OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email));
        await env.Sut.CompleteRegistrationAsync(request);
        await env.Sut.CompleteRegistrationAsync(request);
        env.UserCount().Should().Be(1); env.GoTrue.SignInCalls.Should().Be(2);
    }

    [Test]
    public async Task Completion_UsernameRaceAndMissingRoleBecomeDurableCompensationRequired()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: identity); env.AddUser(request.Username);
        var pending = () => env.Sut.CompleteRegistrationAsync(request);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.CompensationRequired);
    }

    [Test]
    public async Task Completion_StaleFinalizerWithoutProfileReturnsToIdentityConfirmedThenClaimsAndCompletes()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); var staleLease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.FinalizingProfile, staleLease, DateTimeOffset.UtcNow.AddMinutes(-1), identity);
        env.GoTrue.OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email));
        await env.Sut.CompleteRegistrationAsync(request);
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Completed);
        env.UserCount().Should().Be(1);
    }

    [Test]
    public async Task StaleFinalizer_ChangedLeaseIsNotMutated()
    {
        await using var env = new Environment();
        var request = Request(); var owner = Guid.NewGuid(); var caller = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.FinalizingProfile, owner, DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid());
        var returned = await ReturnIdentityConfirmedIfOwnedAsync(env.Sut, request.RegistrationOperationId, caller);
        returned.Should().BeFalse();
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.FinalizingProfile); operation.LeaseToken.Should().Be(owner);
    }

    [TestCase("full-name")]
    [TestCase("inactive")]
    [TestCase("wrong-role")]
    public async Task Completion_InvalidExactReferenceIsStableProfileConflict(string invalidity)
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); var lease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.FinalizingProfile, lease, DateTimeOffset.UtcNow.AddMinutes(-1), identity);
        var operation = env.Load(request.RegistrationOperationId);
        env.AddProfile(operation, fullName: invalidity == "full-name" ? "Other" : operation.FullName,
            isActive: invalidity != "inactive", roleId: invalidity == "wrong-role" ? 9 : 2);
        if (invalidity == "wrong-role") env.AddRole(9, "Admin");
        var conflict = () => env.Sut.CompleteRegistrationAsync(request);
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_profile_conflict");
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Conflict);
    }

    [Test]
    public async Task Completion_TwoProfileReferencesAreCorruptionAndNeverDeleted()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); var lease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.FinalizingProfile, lease, DateTimeOffset.UtcNow.AddMinutes(-1), identity);
        var operation = env.Load(request.RegistrationOperationId);
        env.AddProfile(operation); env.AddProfile(operation, id: Guid.NewGuid());
        var conflict = () => env.Sut.CompleteRegistrationAsync(request);
        (await conflict.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_profile_conflict");
        env.UserCount().Should().Be(2); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Conflict);
    }

    [Test]
    public async Task Completion_MissingStudentRoleMarksCompensationRequired()
    {
        await using var env = new Environment();
        var request = Request(); env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid());
        env.RemoveStudentRole();
        var pending = () => env.Sut.CompleteRegistrationAsync(request);
        (await pending.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_pending");
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.CompensationRequired);
    }

    [Test]
    public async Task ProfileClaimCancellation_ResolvesFreshStateAndRethrowsOriginalCancellation()
    {
        await using var env = new Environment(new ProfileClaimCancellationInterceptor(afterSave: false));
        var request = Request(); env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid());
        var cancelled = () => env.Sut.CompleteRegistrationAsync(request);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.IdentityConfirmed);
    }

    [Test]
    public async Task ProfileClaimPostCommitCancellation_LeavesCallerLeaseAndRethrowsOriginalCancellation()
    {
        await using var env = new Environment(new ProfileClaimCancellationInterceptor(afterSave: true));
        var request = Request(); env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid());
        var cancelled = () => env.Sut.CompleteRegistrationAsync(request);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.FinalizingProfile); operation.LeaseToken.Should().NotBeNull();
    }

    [Test]
    public async Task CompletedWriteFailure_AfterValidSessionReturnsResponseAndLeavesProfileCommitted()
    {
        await using var env = new Environment(new CompletedWriteFailureInterceptor());
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.ProfileCommitted, identityId: identity);
        env.AddExactProfile(env.Load(request.RegistrationOperationId));
        env.GoTrue.OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email));
        var response = await env.Sut.CompleteRegistrationAsync(request);
        response.User.Id.Should().Be(env.Load(request.RegistrationOperationId).ProfileUserId);
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.ProfileCommitted);
    }

    [Test]
    public async Task ProfileCommitPostSaveCancellation_LeavesExactProfileCommittedWithoutDuplication()
    {
        await using var env = new Environment(new ProfileCommitPostSaveCancellationInterceptor());
        var request = Request(); env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid());
        var cancelled = () => env.Sut.CompleteRegistrationAsync(request);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        env.UserCount().Should().Be(1); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.ProfileCommitted);
    }

    [Test]
    public async Task AdvanceExactProfile_InMemoryMatchingLeaseCommitsProfile()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); var lease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.FinalizingProfile, lease, DateTimeOffset.UtcNow.AddMinutes(1), identity);
        env.AddExactProfile(env.Load(request.RegistrationOperationId));
        var advanced = await AdvanceExactProfileAsync(env.Sut, request.RegistrationOperationId, identity, lease);
        advanced!.Status.Should().Be(RegistrationOperation.ProfileCommitted);
        env.Load(request.RegistrationOperationId).LeaseToken.Should().BeNull();
    }

    [Test]
    public async Task AdvanceExactProfile_InMemoryChangedLeaseReturnsNullAndPreservesOwner()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); var ownerLease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.FinalizingProfile, ownerLease, DateTimeOffset.UtcNow.AddMinutes(1), identity);
        env.AddExactProfile(env.Load(request.RegistrationOperationId));
        var advanced = await AdvanceExactProfileAsync(env.Sut, request.RegistrationOperationId, identity, Guid.NewGuid());
        advanced.Should().BeNull();
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.FinalizingProfile); operation.LeaseToken.Should().Be(ownerLease);
    }

    [Test]
    public async Task Compensation_ExactProfileNeverDeletesAndForwardRecovers()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        env.AddExactProfile(env.Load(request.RegistrationOperationId));
        env.GoTrue.OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email));

        (await env.Sut.RegisterAsync(request)).User.Id.Should().Be(env.Load(request.RegistrationOperationId).ProfileUserId);
        env.GoTrue.DeleteCalls.Should().Be(0);
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Completed);
    }

    [Test]
    public async Task Compensation_MultipleReferencesConflictsWithoutDelete()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        var operation = env.Load(request.RegistrationOperationId); env.AddExactProfile(operation); env.AddProfile(operation, id: Guid.NewGuid());

        var act = () => env.Sut.RegisterAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_profile_conflict");
        env.GoTrue.DeleteCalls.Should().Be(0); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Conflict);
    }

    [Test]
    public async Task Compensation_ExactOwnedIdentityDeletesOnceAndPreservesOriginalFailure()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity));
        env.GoTrue.OnDelete = (_, _) => Task.CompletedTask;

        var act = () => env.Sut.RegisterAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("username_taken");
        env.GoTrue.DeleteCalls.Should().Be(1); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Compensated);
    }

    [Test]
    public async Task Compensation_InMemoryLostPreDeleteFenceDoesNotDelete()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); var replacementLease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) =>
        {
            env.ReplaceCompensationLease(request.RegistrationOperationId, replacementLease);
            return Task.FromResult<GoTrueUser?>(Owned(request, identity));
        };

        await CatchAuthAsync(env.Sut.RegisterAsync(request));

        env.GoTrue.DeleteCalls.Should().Be(0);
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.Compensating);
        operation.LeaseToken.Should().Be(replacementLease);
    }

    [Test]
    public async Task Compensation_InMemoryStaleRequiredObservationCannotBypassNewBackoff()
    {
        await using var env = new Environment();
        var request = Request();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: Guid.NewGuid(), lastErrorCode: "username_taken");
        var observed = env.Load(request.RegistrationOperationId);
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        env.SetCompensationBackoff(request.RegistrationOperationId, retryAt);

        (await TryClaimCompensationAsync(env.Sut, observed, Guid.NewGuid())).Should().BeFalse();
        env.Load(request.RegistrationOperationId).NextAttemptAt.Should().Be(retryAt);
    }

    [Test]
    public async Task Compensation_ProviderAbsentMarksCompensatedWithoutDelete()
    {
        await using var env = new Environment();
        var request = Request(); env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: Guid.NewGuid(), lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(null);

        var act = () => env.Sut.RegisterAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("username_taken");
        env.GoTrue.DeleteCalls.Should().Be(0); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Compensated);
    }

    [TestCase("user")]
    [TestCase("app")]
    [TestCase("wrong")]
    public async Task Compensation_MarkerMismatchNeverDeletes(string marker)
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity, marker));

        var act = () => env.Sut.RegisterAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_identity_conflict");
        env.GoTrue.DeleteCalls.Should().Be(0); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Conflict);
    }

    [Test]
    public async Task Compensation_DeleteFailureWithOwnedIdentityPersistsRetry()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity));
        env.GoTrue.OnDelete = (_, _) => throw new AuthException(503, "provider_failure", "sanitized");

        var act = () => env.Sut.RegisterAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_cleanup_pending");
        var operation = env.Load(request.RegistrationOperationId); operation.Status.Should().Be(RegistrationOperation.CompensationRequired); operation.NextAttemptAt.Should().NotBeNull(); operation.LastErrorCode.Should().Be("username_taken");
    }

    [TestCase("auth")]
    [TestCase("transport")]
    public async Task Compensation_LookupFailureAndRepeatedResolutionFailureRemainDurablyPending(string failure)
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) => failure == "auth"
            ? throw new AuthException(503, "registration_identity_lookup_failed", "sanitized")
            : throw new InvalidOperationException("transport");

        var act = () => env.Sut.RegisterAsync(request);
        (await act.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_cleanup_pending");
        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CompensationRequired); operation.LeaseToken.Should().BeNull();
        operation.NextAttemptAt.Should().NotBeNull(); operation.LastErrorCode.Should().Be("username_taken"); env.GoTrue.DeleteCalls.Should().Be(0);
    }

    [Test]
    public async Task Compensation_StaleLeaseResolvesExactProfileBeforeAnyDelete()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.Compensating, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), identity, "username_taken");
        env.AddExactProfile(env.Load(request.RegistrationOperationId));
        env.GoTrue.OnSignIn = (_, _, _) => Task.FromResult(Session(identity, request.Email));

        await env.Sut.RegisterAsync(request);
        env.GoTrue.DeleteCalls.Should().Be(0); env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Completed);
    }

    [Test]
    public async Task Compensation_DeleteCancellationResolvesIndependentlyAndRethrowsOriginalCancellation()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); using var cancellation = new CancellationTokenSource();
        env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        var lookups = 0;
        env.GoTrue.Lookup = (_, token) => ++lookups == 1 ? Task.FromResult<GoTrueUser?>(Owned(request, identity)) : Task.FromResult<GoTrueUser?>(null);
        env.GoTrue.OnDelete = (_, _) => throw new OperationCanceledException(cancellation.Token);

        var act = () => env.Sut.RegisterAsync(request, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Compensated);
        lookups.Should().BeGreaterThan(1);
    }

    [Test]
    public async Task Compensation_ConcurrentClaimsCallProviderDeleteOnlyOnce()
    {
        await using var env = new Environment();
        var request = Request(); var identity = Guid.NewGuid(); env.AddOperation(request, RegistrationOperation.CompensationRequired, identityId: identity, lastErrorCode: "username_taken");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(Owned(request, identity));
        env.GoTrue.OnDelete = async (_, _) => { entered.SetResult(); await release.Task; };
        var first = env.Sut.RegisterAsync(request);
        await entered.Task;
        var second = () => env.Sut.RegisterAsync(request);
        (await second.Should().ThrowAsync<AuthException>()).Which.Code.Should().Be("registration_cleanup_pending");
        release.SetResult();
        var firstError = await CatchAuthAsync(first);
        firstError.Code.Should().Be("username_taken");
        env.GoTrue.DeleteCalls.Should().Be(1);
    }

    [TestCase("exact", RegistrationOperation.IdentityConfirmed)]
    [TestCase("absent", RegistrationOperation.Prepared)]
    [TestCase("unrelated", RegistrationOperation.Conflict)]
    [TestCase("failure", RegistrationOperation.CreatingIdentity)]
    public async Task ReconcileAsync_StaleCreatingIdentity_UsesOnlyAuthoritativeLookup(string outcome, string expectedStatus)
    {
        await using var env = new Environment();
        var request = Request();
        env.AddOperation(request, RegistrationOperation.CreatingIdentity, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));
        env.GoTrue.Lookup = (_, _) => outcome switch
        {
            "exact" => Task.FromResult<GoTrueUser?>(Owned(request)),
            "absent" => Task.FromResult<GoTrueUser?>(null),
            "unrelated" => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = request.Email.Trim() }),
            _ => throw new InvalidOperationException("provider response must remain private"),
        };

        await env.Sut.ReconcileAsync(request.RegistrationOperationId);

        env.Load(request.RegistrationOperationId).Status.Should().Be(expectedStatus);
        env.GoTrue.CreateCalls.Should().Be(0);
        env.GoTrue.SignInCalls.Should().Be(0);
    }

    [Test]
    public async Task ReconcileAsync_IdentityConfirmed_FinalizesProfileWithoutPasswordOrSession()
    {
        await using var env = new Environment();
        var request = Request(); env.AddFreePlan();
        env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid());

        await env.Sut.ReconcileAsync(request.RegistrationOperationId);

        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.ProfileCommitted);
        env.UserCount().Should().Be(1); env.GoTrue.SignInCalls.Should().Be(0); env.GoTrue.CreateCalls.Should().Be(0);
    }

    [Test]
    public async Task ReconcileAsync_IdentityConfirmedWithFutureRetry_IsNotFinalizedEarly()
    {
        await using var env = new Environment();
        var request = Request();
        env.AddOperation(request, RegistrationOperation.IdentityConfirmed, identityId: Guid.NewGuid(), nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(1));

        await env.Sut.ReconcileAsync(request.RegistrationOperationId);

        env.Load(request.RegistrationOperationId).Status.Should().Be(RegistrationOperation.IdentityConfirmed);
        env.UserCount().Should().Be(0); env.GoTrue.SignInCalls.Should().Be(0);
    }

    [Test]
    public async Task ReconcileAsync_StaleFinalizingProfile_RecoversExactOrResetsAndUnexpiredLeaseIsUntouched()
    {
        await using var env = new Environment();
        var exact = Request(); var identity = Guid.NewGuid();
        env.AddOperation(exact, RegistrationOperation.FinalizingProfile, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), identity);
        env.AddExactProfile(env.Load(exact.RegistrationOperationId));
        await env.Sut.ReconcileAsync(exact.RegistrationOperationId);
        env.Load(exact.RegistrationOperationId).Status.Should().Be(RegistrationOperation.ProfileCommitted);

        var missing = Request("missing@example.test", "missing");
        env.AddOperation(missing, RegistrationOperation.FinalizingProfile, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid());
        await env.Sut.ReconcileAsync(missing.RegistrationOperationId);
        env.Load(missing.RegistrationOperationId).Status.Should().Be(RegistrationOperation.ProfileCommitted);

        var conflict = Request("profile-conflict@example.test", "profileconflict");
        env.AddOperation(conflict, RegistrationOperation.FinalizingProfile, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid());
        env.AddProfile(env.Load(conflict.RegistrationOperationId), fullName: "different");
        await env.Sut.ReconcileAsync(conflict.RegistrationOperationId);
        env.Load(conflict.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Conflict);

        var active = Request("active@example.test", "active"); var lease = Guid.NewGuid();
        env.AddOperation(active, RegistrationOperation.FinalizingProfile, lease, DateTimeOffset.UtcNow.AddMinutes(1), Guid.NewGuid());
        await env.Sut.ReconcileAsync(active.RegistrationOperationId);
        env.Load(active.RegistrationOperationId).LeaseToken.Should().Be(lease);
    }

    [Test]
    public async Task ReconcileAsync_CompensationDueAndStale_RecoversWithoutProcessingBackoffOrActiveLease()
    {
        await using var env = new Environment();
        var due = Request(); env.AddOperation(due, RegistrationOperation.CompensationRequired, identityId: Guid.NewGuid(), lastErrorCode: "username_taken");
        env.GoTrue.Lookup = (_, _) => Task.FromResult<GoTrueUser?>(null);
        await env.Sut.ReconcileAsync(due.RegistrationOperationId);
        env.Load(due.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Compensated);

        var stale = Request("stale-compensating@example.test", "stalecompensating");
        env.AddOperation(stale, RegistrationOperation.Compensating, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid());
        await env.Sut.ReconcileAsync(stale.RegistrationOperationId);
        env.Load(stale.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Compensated);

        var deferred = Request("deferred@example.test", "deferred");
        env.AddOperation(deferred, RegistrationOperation.CompensationRequired, identityId: Guid.NewGuid(), lastErrorCode: "username_taken", nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(1));
        await env.Sut.ReconcileAsync(deferred.RegistrationOperationId);
        env.Load(deferred.RegistrationOperationId).Status.Should().Be(RegistrationOperation.CompensationRequired);

        var active = Request("compensating@example.test", "compensating"); var lease = Guid.NewGuid();
        env.AddOperation(active, RegistrationOperation.Compensating, lease, DateTimeOffset.UtcNow.AddMinutes(1), Guid.NewGuid());
        await env.Sut.ReconcileAsync(active.RegistrationOperationId);
        env.Load(active.RegistrationOperationId).LeaseToken.Should().Be(lease);
    }

    [Test]
    public async Task ReconcileAsync_ExpiresOnlyPreparedRowsOlderThanRetentionWithExactTimestampCas()
    {
        await using var env = new Environment();
        var expired = Request();
        env.AddOperation(expired, RegistrationOperation.Prepared, updatedAt: DateTimeOffset.UtcNow.AddHours(-25));
        var fresh = Request("fresh@example.test", "fresh");
        env.AddOperation(fresh, RegistrationOperation.Prepared, updatedAt: DateTimeOffset.UtcNow.AddHours(-23));

        await env.Sut.ReconcileAsync(expired.RegistrationOperationId);
        await env.Sut.ReconcileAsync(fresh.RegistrationOperationId);

        env.Load(expired.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Expired);
        env.Load(expired.RegistrationOperationId).CompletedAt.Should().NotBeNull();
        env.Load(fresh.RegistrationOperationId).Status.Should().Be(RegistrationOperation.Prepared);
    }

    [TestCase("absent")]
    [TestCase("retry")]
    [TestCase("conflict")]
    public async Task ReconcileAsync_OldIdentityRecoveryOwner_CannotOverwriteNewLease(string outcome)
    {
        await using var env = new Environment();
        var request = Request(); var oldLease = Guid.NewGuid(); var newLease = Guid.NewGuid();
        env.AddOperation(request, RegistrationOperation.CreatingIdentity, oldLease, DateTimeOffset.UtcNow.AddMinutes(-1));
        env.GoTrue.Lookup = (_, _) =>
        {
            env.ReplaceIdentityLease(request.RegistrationOperationId, newLease);
            return outcome switch
            {
                "absent" => Task.FromResult<GoTrueUser?>(null),
                "retry" => throw new InvalidOperationException("provider response must remain private"),
                _ => Task.FromResult<GoTrueUser?>(new GoTrueUser { Id = Guid.NewGuid(), Email = request.Email.Trim() }),
            };
        };

        await env.Sut.ReconcileAsync(request.RegistrationOperationId);

        var operation = env.Load(request.RegistrationOperationId);
        operation.Status.Should().Be(RegistrationOperation.CreatingIdentity);
        operation.LeaseToken.Should().Be(newLease);
    }

    private static RegisterRequest Request(string email = " Alice@example.test ", string username = "alice", string fullName = " Alice ") => new()
    { RegistrationOperationId = Guid.NewGuid(), Email = email, Username = username, FullName = fullName, Password = "Password!1" };
    private static GoTrueUser Owned(RegisterRequest request, string? marker = null) => Owned(request, Guid.NewGuid(), marker);
    private static GoTrueUser Owned(RegisterRequest request, Guid identity, string? marker = null)
    {
        var id = request.RegistrationOperationId;
        var user = marker == "app" ? Guid.NewGuid() : marker == "wrong" ? Guid.NewGuid() : id;
        var app = marker == "user" ? Guid.NewGuid() : marker == "wrong" ? Guid.NewGuid() : id;
        return new GoTrueUser { Id = identity, Email = request.Email.Trim().ToLowerInvariant(), UserMetadata = marker == "app" ? null : Marker(user), AppMetadata = marker == "user" ? null : Marker(app) };
    }
    private static GoTrueSession Session(Guid identity, string email) => new() { AccessToken = "access", RefreshToken = "refresh", ExpiresIn = 900, User = new GoTrueUser { Id = identity, Email = email.Trim().ToLowerInvariant() } };
    private static Dictionary<string, object?> Marker(Guid id) => new() { [GoTrueMetadata.RegistrationOperationIdKey] = id.ToString() };
    private static async Task<AuthException> CatchAuthAsync(Task task) { try { await task; } catch (AuthException error) { return error; } throw new AssertionException("Expected AuthException."); }
    private static Task<RegistrationOperation?> ResolveConfirmationAmbiguityAsync(RegistrationCoordinator coordinator, Guid id, Guid lease, Guid identityId) =>
        (Task<RegistrationOperation?>)typeof(RegistrationCoordinator).GetMethod("ResolveConfirmationAmbiguityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [id, lease, identityId, CancellationToken.None])!;
    private static Task<RegistrationOperation?> ConfirmAsync(RegistrationCoordinator coordinator, Guid id, Guid lease, Guid identityId, CancellationToken ct) =>
        (Task<RegistrationOperation?>)typeof(RegistrationCoordinator).GetMethod("ConfirmAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [id, lease, identityId, ct])!;
    private static Task<bool> ReturnIdentityConfirmedIfOwnedAsync(RegistrationCoordinator coordinator, Guid id, Guid lease) =>
        (Task<bool>)typeof(RegistrationCoordinator).GetMethod("ReturnIdentityConfirmedIfOwnedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [id, lease, CancellationToken.None])!;
    private static Task<RegistrationOperation?> AdvanceExactProfileAsync(RegistrationCoordinator coordinator, Guid id, Guid identityId, Guid lease) =>
        (Task<RegistrationOperation?>)typeof(RegistrationCoordinator).GetMethod("AdvanceExactProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [id, identityId, lease, CancellationToken.None])!;
    private static Task<bool> TryClaimCompensationAsync(RegistrationCoordinator coordinator, RegistrationOperation observed, Guid lease) =>
        (Task<bool>)typeof(RegistrationCoordinator).GetMethod("TryClaimCompensationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [observed, lease, CancellationToken.None])!;

    private sealed class Environment : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public RecordingGoTrue GoTrue { get; } = new();
        public CountingPolicy Policy { get; } = new();
        public RegistrationCoordinator Sut { get; }
        public Environment(IInterceptor? interceptor = null)
        {
            var services = new ServiceCollection();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"registration-{Guid.NewGuid():N}");
            if (interceptor is not null) options.AddInterceptors(interceptor);
            services.AddSingleton(options.Options);
            services.AddScoped<AppDbContext, RegistrationTestDbContext>(); services.AddScoped<ISelfRegistrationPolicy>(_ => Policy);
            _provider = services.BuildServiceProvider(validateScopes: true);
            Sut = new RegistrationCoordinator(_provider.GetRequiredService<IServiceScopeFactory>(), GoTrue, NullLogger<RegistrationCoordinator>.Instance);
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Roles.Add(new Role { Id = 2, RoleName = Role.StudentRoleName, CreatedAt = DateTimeOffset.UtcNow });
            db.SaveChanges();
        }
        public RegistrationOperation Load(Guid id) { using var scope = _provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>().RegistrationOperations.Single(item => item.Id == id); }
        public int UserCount() { using var scope = _provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Count(); }
        public int UserPlanCount() { using var scope = _provider.CreateScope(); return scope.ServiceProvider.GetRequiredService<AppDbContext>().UserPlans.Count(); }
        public void AddUser(string username) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Users.Add(new User { Id = Guid.NewGuid(), SupabaseUserId = Guid.NewGuid(), RoleId = 2, Username = username, FullName = username, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); db.SaveChanges(); }
        public void AddFreePlan() { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Plans.Add(new Plan { Id = Guid.NewGuid(), PlanKey = "free", DisplayName = "Free", IsActive = true, CreatedAt = DateTimeOffset.UtcNow }); db.SaveChanges(); }
        public void AddExactProfile(RegistrationOperation operation) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Users.Add(new User { Id = operation.ProfileUserId, SupabaseUserId = operation.IdentityId!.Value, RoleId = 2, Username = operation.Username, FullName = operation.FullName, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); db.SaveChanges(); }
        public void AddProfile(RegistrationOperation operation, Guid? id = null, string? fullName = null, bool isActive = true, int roleId = 2) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Users.Add(new User { Id = id ?? operation.ProfileUserId, SupabaseUserId = operation.IdentityId!.Value, RoleId = roleId, Username = operation.Username, FullName = fullName ?? operation.FullName, IsActive = isActive, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); db.SaveChanges(); }
        public void AddRole(int id, string name) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Roles.Add(new Role { Id = id, RoleName = name, CreatedAt = DateTimeOffset.UtcNow }); db.SaveChanges(); }
        public void RemoveStudentRole() { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Roles.RemoveRange(db.Roles.Where(role => role.RoleName == Role.StudentRoleName)); db.SaveChanges(); }
        public void AddOperation(RegisterRequest request, string status, Guid? lease = null, DateTimeOffset? leaseExpires = null, Guid? identityId = null, string? lastErrorCode = null, DateTimeOffset? nextAttemptAt = null, DateTimeOffset? updatedAt = null) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = DateTimeOffset.UtcNow; db.RegistrationOperations.Add(new RegistrationOperation { Id = request.RegistrationOperationId, NormalizedEmail = request.Email.Trim().ToLowerInvariant(), Username = request.Username.Trim(), FullName = request.FullName.Trim(), ProfileUserId = Guid.NewGuid(), Status = status, LeaseToken = lease, LeaseExpiresAt = leaseExpires, IdentityId = identityId, LastErrorCode = lastErrorCode, NextAttemptAt = nextAttemptAt, CreatedAt = now, UpdatedAt = updatedAt ?? now }); db.SaveChanges(); }
        public void ReplaceIdentityLease(Guid id, Guid lease) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var operation = db.RegistrationOperations.Single(item => item.Id == id); operation.Status = RegistrationOperation.CreatingIdentity; operation.LeaseToken = lease; operation.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1); operation.NextAttemptAt = null; db.SaveChanges(); }
        public void ReplaceCompensationLease(Guid id, Guid lease) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var operation = db.RegistrationOperations.Single(item => item.Id == id); operation.Status = RegistrationOperation.Compensating; operation.LeaseToken = lease; operation.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1); operation.NextAttemptAt = null; db.SaveChanges(); }
        public void SetCompensationBackoff(Guid id, DateTimeOffset retryAt) { using var scope = _provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var operation = db.RegistrationOperations.Single(item => item.Id == id); operation.Status = RegistrationOperation.CompensationRequired; operation.LeaseToken = null; operation.LeaseExpiresAt = null; operation.NextAttemptAt = retryAt; db.SaveChanges(); }
        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }
    private sealed class RegistrationTestDbContext : AppDbContext { public RegistrationTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { } protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentChunk>(); modelBuilder.Ignore<Document>(); modelBuilder.Ignore<Folder>(); } }
    private sealed class TranslationDbContext : AppDbContext { public TranslationDbContext(DbContextOptions<AppDbContext> options) : base(options) { } protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentChunk>(); modelBuilder.Ignore<Document>(); modelBuilder.Ignore<Folder>(); } }
    private sealed class CountingPolicy : ISelfRegistrationPolicy { public int Calls { get; private set; } public Task EnsureAllowedAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; } }
    private sealed class ConfirmationWriteFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.IdentityConfirmed) == true)
                throw new DbUpdateException("confirmation write fault");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ConfirmationCancellationInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.IdentityConfirmed) == true)
                throw new OperationCanceledException("confirmation save cancelled");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ConfirmationPostCommitCancellationInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.IdentityConfirmed) == true)
                throw new OperationCanceledException("confirmation post-commit cancelled");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ConfirmationResolutionFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var operation = eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Select(entry => entry.Entity).SingleOrDefault();
            if (operation?.Status == RegistrationOperation.IdentityConfirmed) throw new OperationCanceledException("confirmation cancelled");
            if (operation?.LastErrorCode == "identity_confirmation_pending") throw new DbUpdateException("resolution mutation fault");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ClaimWriteFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.CreatingIdentity && entry.Entity.LeaseToken.HasValue) == true)
                throw new DbUpdateException("claim write fault");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ProfileClaimCancellationInterceptor(bool afterSave) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!afterSave && eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.FinalizingProfile) == true)
                throw new OperationCanceledException("profile claim cancelled");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (afterSave && eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.FinalizingProfile) == true)
                throw new OperationCanceledException("profile claim committed then cancelled");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class CompletedWriteFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.Completed) == true)
                throw new DbUpdateException("completed write fault");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ProfileCommitPostSaveCancellationInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<RegistrationOperation>().Any(entry => entry.Entity.Status == RegistrationOperation.ProfileCommitted) == true)
                throw new OperationCanceledException("profile commit response cancelled");
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class RecordingGoTrue : IGoTrueClient
    {
        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int SignInCalls { get; private set; }
        public Func<string, string, Dictionary<string, object?>?, Dictionary<string, object?>?, CancellationToken, Task<GoTrueUser>> OnCreate { get; set; } = (_, _, _, _, _) => Task.FromResult(new GoTrueUser());
        public Func<string, CancellationToken, Task<GoTrueUser?>> Lookup { get; set; } = (_, _) => Task.FromResult<GoTrueUser?>(null);
        public Func<Guid, CancellationToken, Task> OnDelete { get; set; } = (_, _) => Task.CompletedTask;
        public Func<string, string, CancellationToken, Task<GoTrueSession>> OnSignIn { get; set; } = (_, _, _) => throw new InvalidOperationException();
        public Task<GoTrueUser> AdminCreateUserAsync(string email, string password, Dictionary<string, object?>? userMetadata, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default) { CreateCalls++; return OnCreate(email, password, userMetadata, appMetadata, cancellationToken); }
        public Task<GoTrueUser?> AdminGetUserByEmailAsync(string email, CancellationToken cancellationToken = default) => Lookup(email, cancellationToken);
        public Task<GoTrueSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) { SignInCalls++; return OnSignIn(email, password, cancellationToken); } public Task<GoTrueSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task SignOutAsync(string accessToken, bool global, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task<GoTrueUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task<GoTrueUser> UpdateUserAsync(string accessToken, string? email, string? password, Dictionary<string, object?>? metadata, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task AdminDeleteUserAsync(Guid userId, CancellationToken cancellationToken = default) { DeleteCalls++; return OnDelete(userId, cancellationToken); } public Task<GoTrueUser> AdminUpdateUserByIdAsync(Guid userId, Dictionary<string, object?>? appMetadata, CancellationToken cancellationToken = default) => throw new NotImplementedException(); public Task AdminSignOutUserAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
