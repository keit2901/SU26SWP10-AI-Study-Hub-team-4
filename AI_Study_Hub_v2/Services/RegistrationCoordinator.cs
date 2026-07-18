using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Durable, marker-owned self-registration coordinator.  It is the production
/// owner of identity creation, local profile finalization, and safe compensation.
/// </summary>
public interface IRegistrationCoordinator
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task ReconcileAsync(Guid operationId, CancellationToken cancellationToken = default);
}

public sealed class RegistrationCoordinator : IRegistrationCoordinator
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    // EF InMemory has no database-side CAS. These gates serialize fallback recovery
    // paths per operation; relational deployments use exact ExecuteUpdate predicates.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryOperationGates = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InMemoryCompensationClaims = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGoTrueClient _goTrue;
    private readonly ILogger<RegistrationCoordinator> _logger;

    public RegistrationCoordinator(IServiceScopeFactory scopeFactory, IGoTrueClient goTrue, ILogger<RegistrationCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _goTrue = goTrue;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Every transition is durable; the loop only bridges a bounded number of
        // local transitions and never turns a client retry into an unbounded worker.
        {
            RegistrationOperation operation;
            try
            {
                operation = await PrepareIdentityAsync(request, cancellationToken);
            }
            catch (AuthException error) when (error.Code == "registration_pending")
            {
                operation = await LoadAsync(request.RegistrationOperationId, CancellationToken.None) ?? throw Pending();
            }

            if (operation.Status is RegistrationOperation.CompensationRequired or RegistrationOperation.Compensating)
            {
                var cleaned = await CompensateAsync(operation.Id, cancellationToken);
                if (cleaned.Status is RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed)
                    return await CompleteRegistrationAsync(request, cancellationToken);
                if (cleaned.Status == RegistrationOperation.Compensated) throw OriginalFailure(cleaned);
                if (cleaned.Status == RegistrationOperation.Conflict) throw Terminal(cleaned.LastErrorCode);
                throw CleanupPending();
            }
            if (operation.Status == RegistrationOperation.Compensated) throw OriginalFailure(operation);
            if (operation.Status == RegistrationOperation.Expired) throw Expired();
            if (operation.Status == RegistrationOperation.Conflict) throw Terminal(operation.LastErrorCode);

            try
            {
                return await CompleteRegistrationAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Finalization has its own ownership-aware resolution.  If it entered
                // compensation, resolve it independently without replacing cancellation.
                await ResolveCompensationAfterCancellationAsync(request.RegistrationOperationId);
                throw;
            }
            catch (AuthException error) when (error.Code == "registration_pending")
            {
                operation = await LoadAsync(request.RegistrationOperationId, CancellationToken.None) ?? throw Pending();
                if (operation.Status is RegistrationOperation.CompensationRequired or RegistrationOperation.Compensating)
                {
                    var cleaned = await CompensateAsync(operation.Id, CancellationToken.None);
                    if (cleaned.Status is RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed)
                        return await CompleteRegistrationAsync(request, cancellationToken);
                    if (cleaned.Status == RegistrationOperation.Compensated) throw OriginalFailure(cleaned);
                    if (cleaned.Status == RegistrationOperation.Conflict) throw Terminal(cleaned.LastErrorCode);
                    throw CleanupPending();
                }
                throw;
            }
        }
        // All bounded paths return or throw; no implicit retry loop is retained here.
    }

    /// <summary>
    /// Advances one durable registration operation without a client password or session.
    /// Every mutation remains state/lease qualified so a scheduler cannot take an active
    /// worker's lease or create a second identity.
    /// </summary>
    public async Task ReconcileAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) return;

        var operation = await LoadAsync(operationId, cancellationToken);
        if (operation is null) return;
        var now = DateTimeOffset.UtcNow;

        try
        {
            switch (operation.Status)
            {
                case RegistrationOperation.Prepared when operation.UpdatedAt <= now.AddHours(-24):
                    await ExpirePreparedIfUnchangedAsync(operation.Id, operation.UpdatedAt, cancellationToken);
                    return;

                case RegistrationOperation.CreatingIdentity when IsLeaseRecoverable(operation, now):
                {
                    var gate = await AcquireInMemoryOperationGateAsync(operation.Id, cancellationToken);
                    try
                    {
                        var lease = Guid.NewGuid();
                        if (!await TryTakeOverIdentityRecoveryAsync(operation, lease, cancellationToken)) return;
                        var claimed = await LoadAsync(operation.Id, cancellationToken);
                        if (claimed is null) return;
                        try { await RecoverCoreAsync(claimed, lease, "identity_reconciliation", cancellationToken); }
                        catch (AuthException) { }
                    }
                    finally { gate?.Release(); }
                    return;
                }

                case RegistrationOperation.IdentityConfirmed when operation.NextAttemptAt is null || operation.NextAttemptAt <= now:
                    await EnsureProfileCommittedAsync(operation, cancellationToken);
                    return;

                case RegistrationOperation.FinalizingProfile when IsLeaseRecoverable(operation, now):
                    await EnsureProfileCommittedAsync(operation, cancellationToken);
                    return;

                case RegistrationOperation.CompensationRequired when operation.NextAttemptAt is null || operation.NextAttemptAt <= now:
                    await CompensateAsync(operation.Id, cancellationToken);
                    return;

                case RegistrationOperation.Compensating when IsLeaseRecoverable(operation, now):
                    await CompensateAsync(operation.Id, cancellationToken);
                    return;

                // ProfileCommitted is deliberately login-ready. Terminal states and active
                // leases require no scheduler work.
                default:
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning("Registration reconciliation is pending for operation {OperationId}: {Code}.", operationId,
                error is AuthException auth ? auth.Code : "reconciliation_failed");
        }
    }

    private static bool IsLeaseRecoverable(RegistrationOperation operation, DateTimeOffset now) =>
        operation.LeaseToken is null || operation.LeaseExpiresAt is null || operation.LeaseExpiresAt <= now;

    private async Task<SemaphoreSlim?> AcquireInMemoryOperationGateAsync(Guid operationId, CancellationToken ct)
    {
        if (!await UsesInMemoryFallbackAsync(ct)) return null;
        var gate = InMemoryOperationGates.GetOrAdd(operationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return gate;
    }

    private async Task<bool> ExpirePreparedIfUnchangedAsync(Guid id, DateTimeOffset updatedAt, CancellationToken ct)
    {
        var gate = await AcquireInMemoryOperationGateAsync(id, ct);
        try
        {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
            return await db.RegistrationOperations.Where(item => item.Id == id && item.Status == RegistrationOperation.Prepared && item.UpdatedAt == updatedAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.Expired)
                    .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null).SetProperty(item => item.CompletedAt, now)
                    .SetProperty(item => item.UpdatedAt, now), ct) == 1;

        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (operation is null || operation.Status != RegistrationOperation.Prepared || operation.UpdatedAt != updatedAt) return false;
        operation.Status = RegistrationOperation.Expired; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
        operation.NextAttemptAt = null; operation.CompletedAt = now; operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
        }
        finally { gate?.Release(); }
    }

    private async Task<bool> TryTakeOverIdentityRecoveryAsync(RegistrationOperation observed, Guid lease, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
            return await db.RegistrationOperations.Where(item => item.Id == observed.Id
                && item.Status == RegistrationOperation.CreatingIdentity && item.LeaseToken == observed.LeaseToken
                && item.LeaseExpiresAt == observed.LeaseExpiresAt
                && (item.LeaseToken == null || item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseToken, lease)
                    .SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration)).SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1).SetProperty(item => item.UpdatedAt, now), ct) == 1;

        var current = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == observed.Id, ct);
        if (current is null || current.Status != RegistrationOperation.CreatingIdentity || current.LeaseToken != observed.LeaseToken
            || current.LeaseExpiresAt != observed.LeaseExpiresAt || !IsLeaseRecoverable(current, now)) return false;
        current.LeaseToken = lease; current.LeaseExpiresAt = now.Add(LeaseDuration); current.NextAttemptAt = null;
        current.AttemptCount++; current.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<RegistrationOperation> PrepareIdentityAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RegistrationOperationId == Guid.Empty)
            throw new AuthException(400, "registration_operation_required", "Registration operation id is required.");

        var payload = new RegistrationPayload(request.RegistrationOperationId, request.Email.Trim().ToLowerInvariant(),
            request.Username.Trim(), request.FullName.Trim());
        var operation = await PrepareAsync(payload, cancellationToken);
        ThrowIfTerminal(operation);
        if (IsIdentityComplete(operation.Status)) return operation;

        // A stale owner may have completed the provider request before crashing.
        if (operation.Status == RegistrationOperation.CreatingIdentity && operation.LeaseExpiresAt > DateTimeOffset.UtcNow)
            throw Pending();
        if (operation.Status == RegistrationOperation.CreatingIdentity)
        {
            var recovered = await RecoverAsync(operation, operation.LeaseToken, "stale_identity_lease");
            if (recovered is not null) return recovered;
            operation = await LoadAsync(payload.Id, CancellationToken.None) ?? throw Pending();
            ThrowIfTerminal(operation);
            if (IsIdentityComplete(operation.Status)) return operation;
            if (operation.Status == RegistrationOperation.Conflict) throw EmailTaken();
            if (operation.Status == RegistrationOperation.CreatingIdentity) throw Pending();
        }

        var lease = Guid.NewGuid();
        if (!await TryClaimAsync(payload.Id, lease, cancellationToken))
        {
            operation = await LoadAsync(payload.Id, CancellationToken.None) ?? throw Pending();
            ThrowIfTerminal(operation);
            if (IsIdentityComplete(operation.Status)) return operation;
            throw Pending();
        }

        GoTrueUser identity;
        try
        {
            identity = await _goTrue.AdminCreateUserAsync(payload.Email, request.Password, Metadata(payload), Marker(payload.Id), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await RecoverAsync(await LoadAsync(payload.Id, recoveryTimeout.Token), lease, "identity_create_cancelled", recoveryTimeout.Token);
            }
            catch (Exception recoveryError)
            {
                _logger.LogWarning("Registration cancellation recovery did not complete for operation {OperationId}: {Code}.", payload.Id,
                    recoveryError is AuthException auth ? auth.Code : "recovery_failed");
            }
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning("Registration identity dispatch requires recovery for operation {OperationId}.", payload.Id);
            return await RecoverAsync(await LoadAsync(payload.Id, CancellationToken.None), lease, "identity_create_ambiguous") ?? throw Pending();
        }
        return await ConfirmCreatedIdentityAsync(payload, lease, identity, cancellationToken);
    }

    /// <summary>
    /// Hidden Phase 2B2a completion path.  Production registration intentionally
    /// remains unwired until compensation/reconciliation are available.
    /// </summary>
    public async Task<AuthResponse> CompleteRegistrationAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var operation = await PrepareIdentityAsync(request, cancellationToken);
        operation = await EnsureProfileCommittedAsync(operation, cancellationToken);
        if (operation.Status == RegistrationOperation.Conflict)
            throw ProfileConflict();
        if (operation.Status is not (RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed))
            throw Pending();

        var user = await LoadStrictExactProfileAsync(operation, cancellationToken);

        GoTrueSession session;
        try
        {
            session = await _goTrue.SignInWithPasswordAsync(operation.NormalizedEmail, request.Password, cancellationToken);
            if (session.User is null || session.User.Id == Guid.Empty || session.User.Id != operation.IdentityId)
                throw SessionUnavailable();
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthException error) when (error.Code == "registration_session_unavailable") { throw; }
        catch (Exception)
        {
            _logger.LogWarning("Registration session is unavailable for operation {OperationId} identity {IdentityId}.", operation.Id, operation.IdentityId);
            throw SessionUnavailable();
        }

        await TryMarkCompletedAsync(operation.Id, cancellationToken);
        return BuildAuthResponse(user, session);
    }

    private async Task<RegistrationOperation> EnsureProfileCommittedAsync(RegistrationOperation operation, CancellationToken ct)
    {
        if (operation.Status is RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed)
        {
            await LoadStrictExactProfileAsync(operation, ct);
            return operation;
        }
        if (operation.Status == RegistrationOperation.FinalizingProfile)
        {
            if (operation.LeaseExpiresAt > DateTimeOffset.UtcNow) throw Pending();
            // A stale finalizer may have committed before it lost its response.  Inspect
            // the deterministic profile first; never overwrite its lease blindly.
            var recovered = await RecoverStaleProfileAsync(operation, ct);
            if (recovered is not null) return recovered;
            if (operation.LeaseToken is not Guid staleLease || !await ReturnIdentityConfirmedIfOwnedAsync(operation.Id, staleLease, ct))
                throw Pending();
            operation = await LoadAsync(operation.Id, ct) ?? throw Pending();
        }
        if (operation.Status != RegistrationOperation.IdentityConfirmed || operation.IdentityId is not Guid identityId || identityId == Guid.Empty)
            throw Pending();

        var lease = Guid.NewGuid();
        if (!await TryClaimProfileAsync(operation.Id, lease, ct)) throw Pending();
        try
        {
            return await FinalizeProfileAsync(operation.Id, lease, ct);
        }
        catch (OperationCanceledException)
        {
            await ResolveProfileAmbiguityBoundedAsync(operation.Id, lease);
            throw;
        }
        catch (AuthException) { throw; }
        catch (Exception)
        {
            var resolved = await ResolveProfileAmbiguityAsync(operation.Id, lease, CancellationToken.None);
            if (resolved is not null) return resolved;
            throw Pending();
        }
    }

    private async Task<bool> TryClaimProfileAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            if (db.Database.IsRelational())
                return await db.RegistrationOperations.Where(item => item.Id == operationId
                    && item.Status == RegistrationOperation.IdentityConfirmed && item.IdentityId != null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.FinalizingProfile)
                        .SetProperty(item => item.LeaseToken, lease).SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration))
                        .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1).SetProperty(item => item.UpdatedAt, now), ct) == 1;
            var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
            if (operation is null || operation.Status != RegistrationOperation.IdentityConfirmed || operation.IdentityId is null) return false;
            operation.Status = RegistrationOperation.FinalizingProfile; operation.LeaseToken = lease;
            operation.LeaseExpiresAt = now.Add(LeaseDuration); operation.AttemptCount++; operation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await ResolveProfileClaimAmbiguityAsync(operationId, lease, timeout.Token);
            }
            catch { }
            throw;
        }
        catch
        {
            return await ResolveProfileClaimAmbiguityAsync(operationId, lease, CancellationToken.None);
        }
    }

    private async Task<bool> ResolveProfileClaimAmbiguityAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        var operation = await LoadAsync(operationId, ct);
        // Resolution is observation-only: it may acknowledge this caller's committed
        // CAS, but never steals or repairs another worker's lease.
        return operation?.Status == RegistrationOperation.FinalizingProfile && operation.LeaseToken == lease;
    }

    private async Task<RegistrationOperation> FinalizeProfileAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var operation = db.Database.IsRelational()
                ? await db.RegistrationOperations.FromSqlInterpolated($"SELECT * FROM registration_operations WHERE id = {operationId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct)
                : await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
            if (operation is null) throw Pending();
            if (operation.Status != RegistrationOperation.FinalizingProfile || operation.LeaseToken != lease || operation.IdentityId is not Guid identityId || identityId == Guid.Empty)
                throw Pending();

            var profiles = await db.Users.Include(user => user.Role).Where(user => user.Id == operation.ProfileUserId || user.SupabaseUserId == identityId).ToListAsync(ct);
            if (profiles.Count != 0)
            {
                var exact = profiles.Count == 1 && IsStrictExactProfile(profiles[0], operation);
                if (!exact)
                    return await FinishProfileStateAsync(db, transaction, operation, RegistrationOperation.Conflict, "registration_profile_conflict", ct);
                return await FinishProfileStateAsync(db, transaction, operation, RegistrationOperation.ProfileCommitted, null, ct);
            }

            if (await db.Users.AnyAsync(user => user.Username == operation.Username, ct))
                return await FinishProfileStateAsync(db, transaction, operation, RegistrationOperation.CompensationRequired, "username_taken", ct);
            var student = await db.Roles.SingleOrDefaultAsync(role => role.RoleName == Role.StudentRoleName, ct);
            if (student is null)
                return await FinishProfileStateAsync(db, transaction, operation, RegistrationOperation.CompensationRequired, "role_not_seeded", ct);

            var now = DateTimeOffset.UtcNow;
            var user = new User { Id = operation.ProfileUserId, SupabaseUserId = identityId, RoleId = student.Id, Role = student,
                Username = operation.Username, FullName = operation.FullName, IsActive = true, TotalTokensUsed = 0, CreatedAt = now, UpdatedAt = now };
            db.Users.Add(user);
            var free = await db.Plans.SingleOrDefaultAsync(plan => plan.PlanKey == "free" && plan.IsActive, ct);
            if (free is not null) db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = user.Id, PlanId = free.Id, Status = "active", AssignedAt = now });
            return await FinishProfileStateAsync(db, transaction, operation, RegistrationOperation.ProfileCommitted, null, ct);
        }
        catch
        {
            if (transaction is not null) try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static async Task<RegistrationOperation> FinishProfileStateAsync(AppDbContext db, IDbContextTransaction? transaction,
        RegistrationOperation operation, string status, string? error, CancellationToken ct)
    {
        operation.Status = status; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
        operation.NextAttemptAt = null; operation.LastErrorCode = error; operation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Snapshot(operation);
    }

    private async Task<RegistrationOperation?> RecoverStaleProfileAsync(RegistrationOperation stale, CancellationToken ct)
    {
        var profiles = await LoadProfileReferencesAsync(stale, ct);
        if (profiles.Count == 0) return null;
        if (profiles.Count != 1 || !IsStrictExactProfile(profiles[0], stale))
        {
            await MarkProfileConflictIfOwnedAsync(stale.Id, stale.LeaseToken, ct);
            throw ProfileConflict();
        }
        if (stale.LeaseToken is not Guid lease) throw Pending();
        return await AdvanceExactProfileAsync(stale.Id, stale.IdentityId!.Value, lease, ct) ?? throw Pending();
    }

    private async Task<RegistrationOperation?> ResolveProfileAmbiguityAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        var operation = await LoadAsync(operationId, ct);
        if (operation is null) return null;
        if (operation.Status is RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed) return operation;
        if (operation.IdentityId is null) return null;
        var profiles = await LoadProfileReferencesAsync(operation, ct);
        if (profiles.Count != 0)
        {
            if (profiles.Count != 1 || !IsStrictExactProfile(profiles[0], operation))
            {
                if (operation.Status == RegistrationOperation.FinalizingProfile && operation.LeaseToken == lease)
                    await MarkProfileConflictIfOwnedAsync(operation.Id, lease, ct);
                throw ProfileConflict();
            }
            return await AdvanceExactProfileAsync(operation.Id, operation.IdentityId.Value, lease, ct);
        }
        if (operation.Status == RegistrationOperation.FinalizingProfile && operation.LeaseToken == lease)
        {
            if (await IsUsernameOwnedByAnotherUserAsync(operation, ct))
            {
                await MarkCompensationRequiredIfOwnedAsync(operation.Id, lease, "username_taken", ct);
                throw Pending();
            }
            await ReturnIdentityConfirmedIfOwnedAsync(operation.Id, lease, ct);
            return null;
        }
        return null;
    }

    private async Task ResolveProfileAmbiguityBoundedAsync(Guid operationId, Guid lease)
    {
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await ResolveProfileAmbiguityAsync(operationId, lease, timeout.Token); }
        catch (Exception error) { _logger.LogWarning("Registration profile cancellation resolution did not complete for operation {OperationId}: {Code}.", operationId, error is AuthException auth ? auth.Code : "resolution_failed"); }
    }

    private async Task<User> LoadStrictExactProfileAsync(RegistrationOperation operation, CancellationToken ct)
    {
        var profiles = await LoadProfileReferencesAsync(operation, ct);
        if (profiles.Count != 1 || !IsStrictExactProfile(profiles[0], operation)) throw ProfileConflict();
        return profiles[0];
    }

    private async Task<List<User>> LoadProfileReferencesAsync(RegistrationOperation operation, CancellationToken ct)
    {
        if (operation.IdentityId is not Guid identityId) return [];
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Include(user => user.Role).AsNoTracking()
            .Where(user => user.Id == operation.ProfileUserId || user.SupabaseUserId == identityId).ToListAsync(ct);
    }

    private static bool IsStrictExactProfile(User user, RegistrationOperation operation) =>
        user.Id == operation.ProfileUserId && user.SupabaseUserId == operation.IdentityId
        && user.Username == operation.Username && user.FullName == operation.FullName
        && user.IsActive && user.Role?.RoleName == Role.StudentRoleName;

    private async Task<RegistrationOperation?> AdvanceExactProfileAsync(Guid operationId, Guid identityId, Guid lease, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            var changed = await db.RegistrationOperations.Where(item => item.Id == operationId
                && item.Status == RegistrationOperation.FinalizingProfile && item.IdentityId == identityId && item.LeaseToken == lease)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.ProfileCommitted)
                    .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.LastErrorCode, (string?)null).SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.UpdatedAt, now), ct);
            return changed == 1 ? await LoadAsync(operationId, ct) : null;
        }
        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
        if (operation is null) return null;
        if (operation.Status is RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed) return Snapshot(operation);
        if (operation.Status != RegistrationOperation.FinalizingProfile || operation.IdentityId != identityId || operation.LeaseToken != lease) return null;
        operation.Status = RegistrationOperation.ProfileCommitted; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
        operation.LastErrorCode = null; operation.NextAttemptAt = null; await db.SaveChangesAsync(ct);
        return Snapshot(operation);
    }

    private async Task<bool> ReturnIdentityConfirmedIfOwnedAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
            return await db.RegistrationOperations.Where(item => item.Id == operationId && item.Status == RegistrationOperation.FinalizingProfile && item.LeaseToken == lease)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.IdentityConfirmed)
                    .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.LastErrorCode, "profile_finalization_pending").SetProperty(item => item.NextAttemptAt, now.AddMinutes(1))
                    .SetProperty(item => item.UpdatedAt, now), ct) == 1;
        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
        if (operation?.Status != RegistrationOperation.FinalizingProfile || operation.LeaseToken != lease) return false;
        operation.Status = RegistrationOperation.IdentityConfirmed; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
        operation.LastErrorCode = "profile_finalization_pending"; operation.NextAttemptAt = now.AddMinutes(1); await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> MarkProfileConflictIfOwnedAsync(Guid operationId, Guid? lease, CancellationToken ct)
    {
        if (lease is not Guid expectedLease) return false;
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
            return await db.RegistrationOperations.Where(item => item.Id == operationId && item.Status == RegistrationOperation.FinalizingProfile && item.LeaseToken == expectedLease)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.Conflict)
                    .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.LastErrorCode, "registration_profile_conflict").SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.UpdatedAt, now), ct) == 1;
        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
        if (operation?.Status != RegistrationOperation.FinalizingProfile || operation.LeaseToken != expectedLease) return false;
        operation.Status = RegistrationOperation.Conflict; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
        operation.LastErrorCode = "registration_profile_conflict"; operation.NextAttemptAt = null; operation.UpdatedAt = now; await db.SaveChangesAsync(ct); return true;
    }

    private async Task<bool> IsUsernameOwnedByAnotherUserAsync(RegistrationOperation operation, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().AnyAsync(user => user.Username == operation.Username && user.Id != operation.ProfileUserId, ct);
    }

    private async Task<RegistrationOperation> CompensateAsync(Guid operationId, CancellationToken requestCt)
    {
        // InMemory cannot enforce database CAS. Its fallback holds this operation gate
        // across claim, external work, and every fallback transition; relational stores
        // rely on their status/lease-qualified ExecuteUpdate predicates instead.
        SemaphoreSlim? gate = (await UsesInMemoryFallbackAsync(requestCt))
            ? InMemoryCompensationClaims.GetOrAdd(operationId, _ => new SemaphoreSlim(1, 1)) : null;
        if (gate is not null && !await gate.WaitAsync(0, requestCt))
            return await LoadAsync(operationId, CancellationToken.None) ?? throw Pending();
        try
        {
            var operation = await LoadAsync(operationId, CancellationToken.None) ?? throw Pending();
            if (operation.Status is RegistrationOperation.Compensated or RegistrationOperation.Conflict) return operation;
            if (operation.Status == RegistrationOperation.Compensating && operation.LeaseExpiresAt > DateTimeOffset.UtcNow) return operation;
            if (operation.Status == RegistrationOperation.CompensationRequired && operation.NextAttemptAt > DateTimeOffset.UtcNow) return operation;

            var lease = Guid.NewGuid();
            if (!await TryClaimCompensationAsync(operation, lease, requestCt))
                return await LoadAsync(operationId, CancellationToken.None) ?? throw Pending();

            try { return await ExecuteCompensationAsync(operationId, lease, requestCt); }
            catch (OperationCanceledException)
            {
                await ResolveCompensationBoundedAsync(operationId, lease);
                throw;
            }
            catch (Exception)
            {
                return await ResolveCompensationFailureAsync(operationId, lease);
            }
        }
        finally { if (gate is not null && gate.CurrentCount == 0) gate.Release(); }
    }

    private async Task<bool> UsesInMemoryFallbackAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return !scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.IsRelational();
    }

    private async Task<bool> TryClaimCompensationAsync(RegistrationOperation observed, Guid lease, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
        {
            IQueryable<RegistrationOperation> query;
            if (observed.Status == RegistrationOperation.Compensating
                && observed.LeaseToken is Guid observedLease
                && observed.LeaseExpiresAt is DateTimeOffset observedExpiry)
            {
                query = db.RegistrationOperations.Where(item => item.Id == observed.Id
                    && item.Status == RegistrationOperation.Compensating && item.LeaseToken == observedLease
                    && item.LeaseExpiresAt == observedExpiry && item.LeaseExpiresAt <= now);
            }
            else if (observed.Status == RegistrationOperation.CompensationRequired)
            {
                query = db.RegistrationOperations.Where(item => item.Id == observed.Id
                    && item.Status == RegistrationOperation.CompensationRequired && item.LeaseToken == null
                    && item.NextAttemptAt == observed.NextAttemptAt
                    && (item.NextAttemptAt == null || item.NextAttemptAt <= now));
            }
            else return false;
            var changed = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, RegistrationOperation.Compensating)
                .SetProperty(item => item.LeaseToken, lease).SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration))
                .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1).SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(item => item.UpdatedAt, now), ct);
            return changed == 1;
        }
        var current = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == observed.Id, ct);
        var mayClaimStaleLease = observed.Status == RegistrationOperation.Compensating
            && observed.LeaseToken is Guid staleLease && observed.LeaseExpiresAt is DateTimeOffset staleExpiry
            && current?.Status == RegistrationOperation.Compensating && current.LeaseToken == staleLease
            && current.LeaseExpiresAt == staleExpiry && current.LeaseExpiresAt <= now;
        var mayClaimDueRetry = observed.Status == RegistrationOperation.CompensationRequired
            && current?.Status == RegistrationOperation.CompensationRequired && current.LeaseToken is null
            && current.NextAttemptAt == observed.NextAttemptAt
            && (current.NextAttemptAt is null || current.NextAttemptAt <= now);
        if (current is null || !mayClaimStaleLease && !mayClaimDueRetry) return false;
        current.Status = RegistrationOperation.Compensating; current.LeaseToken = lease; current.LeaseExpiresAt = now.Add(LeaseDuration);
        current.AttemptCount++; current.NextAttemptAt = null; current.UpdatedAt = now; await db.SaveChangesAsync(ct); return true;
    }

    private async Task<bool> RenewCompensationLeaseForDeleteAsync(Guid operationId, Guid lease, Guid identityId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
            return await db.RegistrationOperations.Where(item => item.Id == operationId
                && item.Status == RegistrationOperation.Compensating && item.LeaseToken == lease && item.IdentityId == identityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration))
                    .SetProperty(item => item.UpdatedAt, now), ct) == 1;

        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
        if (operation?.Status != RegistrationOperation.Compensating || operation.LeaseToken != lease || operation.IdentityId != identityId) return false;
        operation.LeaseExpiresAt = now.Add(LeaseDuration); operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<RegistrationOperation> ExecuteCompensationAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        // Fresh local references are authoritative: provider deletion is allowed only
        // after proving that no local profile can reference the identity.
        var operation = await LoadAsync(operationId, ct) ?? throw Pending();
        if (operation.Status != RegistrationOperation.Compensating || operation.LeaseToken != lease || operation.IdentityId is not Guid identityId || identityId == Guid.Empty)
            return operation;
        var profiles = await LoadProfileReferencesAsync(operation, ct);
        if (profiles.Count == 1 && IsStrictExactProfile(profiles[0], operation))
            return await ForwardRecoveredProfileAsync(operation, lease, ct);
        if (profiles.Count != 0)
            return await MarkCompensationConflictAsync(operationId, lease, "registration_profile_conflict", ct);

        var identity = await _goTrue.AdminGetUserByEmailAsync(operation.NormalizedEmail, ct);
        if (identity is null) return await MarkCompensatedAsync(operationId, lease, ct);
        if (identity.Id != identityId || !IsOwned(identity, new RegistrationPayload(operation.Id, operation.NormalizedEmail, operation.Username, operation.FullName)))
            return await MarkCompensationConflictAsync(operationId, lease,
                string.Equals(identity.Email?.Trim(), operation.NormalizedEmail, StringComparison.OrdinalIgnoreCase) ? "registration_identity_conflict" : "email_already_registered", ct);
        if (!await RenewCompensationLeaseForDeleteAsync(operationId, lease, identityId, ct))
            return await LoadAsync(operationId, ct) ?? throw Pending();

        try
        {
            await _goTrue.AdminDeleteUserAsync(identityId, ct);
            return await MarkCompensatedAsync(operationId, lease, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { return await ResolveDeleteAmbiguityAsync(operationId, lease, CancellationToken.None); }
    }

    private async Task<RegistrationOperation> ResolveDeleteAmbiguityAsync(Guid operationId, Guid lease, CancellationToken ct)
    {
        using var timeout = ct.CanBeCanceled ? null : new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bounded = ct.CanBeCanceled ? ct : timeout!.Token;
        var operation = await LoadAsync(operationId, bounded) ?? throw Pending();
        if (operation.Status != RegistrationOperation.Compensating || operation.LeaseToken != lease || operation.IdentityId is not Guid identityId) return operation;
        var profiles = await LoadProfileReferencesAsync(operation, bounded);
        if (profiles.Count == 1 && IsStrictExactProfile(profiles[0], operation)) return await ForwardRecoveredProfileAsync(operation, lease, bounded);
        if (profiles.Count != 0) return await MarkCompensationConflictAsync(operationId, lease, "registration_profile_conflict", bounded);
        var identity = await _goTrue.AdminGetUserByEmailAsync(operation.NormalizedEmail, bounded);
        if (identity is null) return await MarkCompensatedAsync(operationId, lease, bounded);
        if (identity.Id != identityId || !IsOwned(identity, new RegistrationPayload(operation.Id, operation.NormalizedEmail, operation.Username, operation.FullName)))
            return await MarkCompensationConflictAsync(operationId, lease, "registration_identity_conflict", bounded);
        return await ReturnCompensationRequiredAsync(operationId, lease, bounded);
    }

    private async Task<RegistrationOperation> ResolveCompensationFailureAsync(Guid operationId, Guid lease)
    {
        try
        {
            return await ResolveDeleteAmbiguityAsync(operationId, lease, CancellationToken.None);
        }
        catch (Exception)
        {
            _logger.LogWarning("Registration compensation resolution requires retry for operation {OperationId}.", operationId);
            try
            {
                return await ReturnCompensationRequiredAsync(operationId, lease, CancellationToken.None);
            }
            catch (Exception)
            {
                // A persistence outage can prevent the final CAS; never leak the
                // provider error, and let the durable state be re-observed by retry.
                _logger.LogWarning("Registration compensation retry persistence is pending for operation {OperationId}.", operationId);
                return await LoadAsync(operationId, CancellationToken.None) ?? throw Pending();
            }
        }
    }

    private async Task ResolveCompensationBoundedAsync(Guid operationId, Guid lease)
    {
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await ResolveDeleteAmbiguityAsync(operationId, lease, timeout.Token); }
        catch { _logger.LogWarning("Registration compensation cancellation resolution did not complete for operation {OperationId}.", operationId); }
    }

    private async Task ResolveCompensationAfterCancellationAsync(Guid operationId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var operation = await LoadAsync(operationId, timeout.Token);
            if (operation?.Status == RegistrationOperation.Compensating && operation.LeaseToken is Guid lease)
                await ResolveDeleteAmbiguityAsync(operationId, lease, timeout.Token);
        }
        catch { }
    }

    private async Task<RegistrationOperation> ForwardRecoveredProfileAsync(RegistrationOperation operation, Guid lease, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            var changed = await db.RegistrationOperations.Where(item => item.Id == operation.Id && item.Status == RegistrationOperation.Compensating && item.LeaseToken == lease && item.IdentityId == operation.IdentityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.ProfileCommitted).SetProperty(item => item.LeaseToken, (Guid?)null)
                    .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null).SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null).SetProperty(item => item.UpdatedAt, now), ct);
            return changed == 1 ? await LoadAsync(operation.Id, ct) ?? throw Pending() : await LoadAsync(operation.Id, ct) ?? throw Pending();
        }
        var current = await db.RegistrationOperations.SingleAsync(item => item.Id == operation.Id, ct);
        if (current.Status == RegistrationOperation.Compensating && current.LeaseToken == lease && current.IdentityId == operation.IdentityId)
        { current.Status = RegistrationOperation.ProfileCommitted; current.LeaseToken = null; current.LeaseExpiresAt = null; current.NextAttemptAt = null; current.UpdatedAt = now; await db.SaveChangesAsync(ct); }
        return Snapshot(current);
    }

    private async Task<RegistrationOperation> MarkCompensatedAsync(Guid id, Guid lease, CancellationToken ct) =>
        await MutateCompensationAsync(id, lease, RegistrationOperation.Compensated, null, ct);
    private async Task<RegistrationOperation> ReturnCompensationRequiredAsync(Guid id, Guid lease, CancellationToken ct) =>
        await MutateCompensationAsync(id, lease, RegistrationOperation.CompensationRequired, null, ct);
    private async Task<RegistrationOperation> MarkCompensationConflictAsync(Guid id, Guid lease, string code, CancellationToken ct) =>
        await MutateCompensationAsync(id, lease, RegistrationOperation.Conflict, code, ct);

    private async Task<RegistrationOperation> MutateCompensationAsync(Guid id, Guid lease, string status, string? terminalCode, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = DateTimeOffset.UtcNow;
        var attempts = (await LoadAsync(id, ct))?.AttemptCount ?? 1;
        var next = status == RegistrationOperation.CompensationRequired
            ? now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(6, attempts))))
            : (DateTimeOffset?)null;
        if (db.Database.IsRelational())
        {
            var changed = await db.RegistrationOperations.Where(item => item.Id == id && item.Status == RegistrationOperation.Compensating && item.LeaseToken == lease)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status).SetProperty(item => item.LeaseToken, (Guid?)null)
                    .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null).SetProperty(item => item.NextAttemptAt, next)
                    .SetProperty(item => item.LastErrorCode, item => terminalCode ?? item.LastErrorCode).SetProperty(item => item.CompletedAt, item => status == RegistrationOperation.Compensated ? now : item.CompletedAt).SetProperty(item => item.UpdatedAt, now), ct);
            return await LoadAsync(id, ct) ?? throw Pending();
        }
        var operation = await db.RegistrationOperations.SingleAsync(item => item.Id == id, ct);
        if (operation.Status == RegistrationOperation.Compensating && operation.LeaseToken == lease)
        { operation.Status = status; operation.LeaseToken = null; operation.LeaseExpiresAt = null; operation.NextAttemptAt = next; if (terminalCode is not null) operation.LastErrorCode = terminalCode; if (status == RegistrationOperation.Compensated) operation.CompletedAt = now; operation.UpdatedAt = now; await db.SaveChangesAsync(ct); }
        return Snapshot(operation);
    }

    private async Task<bool> MarkCompensationRequiredIfOwnedAsync(Guid operationId, Guid lease, string errorCode = "profile_finalization_failed", CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
            return await db.RegistrationOperations.Where(item => item.Id == operationId && item.Status == RegistrationOperation.FinalizingProfile && item.LeaseToken == lease)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.CompensationRequired)
                    .SetProperty(item => item.LeaseToken, (Guid?)null).SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.LastErrorCode, errorCode).SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.UpdatedAt, now), ct) == 1;
        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
        if (operation?.Status != RegistrationOperation.FinalizingProfile || operation.LeaseToken != lease) return false;
        operation.Status = RegistrationOperation.CompensationRequired; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
        operation.LastErrorCode = errorCode; operation.NextAttemptAt = null; operation.UpdatedAt = now; await db.SaveChangesAsync(ct); return true;
    }

    private async Task TryMarkCompletedAsync(Guid operationId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
            if (operation?.Status != RegistrationOperation.ProfileCommitted) return;
            operation.Status = RegistrationOperation.Completed; operation.CompletedAt = DateTimeOffset.UtcNow; operation.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        { _logger.LogWarning("Registration completion persistence is pending for operation {OperationId}.", operationId); }
    }

    private static AuthResponse BuildAuthResponse(User user, GoTrueSession session)
    {
        var expiresAt = session.ExpiresAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(session.ExpiresAt) : DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn > 0 ? session.ExpiresIn : 900);
        return new AuthResponse { AccessToken = session.AccessToken, RefreshToken = session.RefreshToken, TokenType = "Bearer",
            ExpiresIn = session.ExpiresIn > 0 ? session.ExpiresIn : (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds, ExpiresAt = expiresAt,
            User = new UserDto { Id = user.Id, Email = session.User?.Email ?? string.Empty, Username = user.Username, FullName = user.FullName,
                Role = user.Role?.RoleName ?? string.Empty, IsActive = user.IsActive, CreatedAt = user.CreatedAt } };
    }

    private async Task<RegistrationOperation> ConfirmCreatedIdentityAsync(RegistrationPayload payload, Guid lease, GoTrueUser identity, CancellationToken ct)
    {
        if (!IsOwned(identity, payload))
            return await RecoverAsync(await LoadAsync(payload.Id, ct), lease, "identity_create_unverified", ct) ?? throw Pending();
        return await ConfirmAsync(payload.Id, lease, identity.Id, ct) ?? throw Pending();
    }

    private async Task<RegistrationOperation> PrepareAsync(RegistrationPayload payload, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.RegistrationOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == payload.Id, ct);
        if (existing is not null)
        {
            if (!Matches(existing, payload)) throw OperationConflict();
            return Snapshot(existing);
        }

        await scope.ServiceProvider.GetRequiredService<ISelfRegistrationPolicy>().EnsureAllowedAsync(ct);
        if (await db.Users.AsNoTracking().AnyAsync(user => user.Username == payload.Username, ct)) throw UsernameTaken();
        if (!await db.Roles.AsNoTracking().AnyAsync(role => role.RoleName == Role.StudentRoleName, ct))
            throw new AuthException(500, "role_not_seeded", "Student role is missing from the database.");
        if (await ActiveOperations(db.RegistrationOperations.AsNoTracking()).AnyAsync(item => item.NormalizedEmail == payload.Email, ct)) throw InProgress();
        if (await ActiveOperations(db.RegistrationOperations.AsNoTracking()).AnyAsync(item => item.Username == payload.Username, ct)) throw UsernameInProgress();

        var now = DateTimeOffset.UtcNow;
        db.RegistrationOperations.Add(new RegistrationOperation
        {
            Id = payload.Id, NormalizedEmail = payload.Email, Username = payload.Username, FullName = payload.FullName,
            ProfileUserId = Guid.NewGuid(), Status = RegistrationOperation.Prepared, CreatedAt = now, UpdatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            return await LoadAsync(payload.Id, CancellationToken.None) ?? throw new AuthException(503, "registration_pending", "Registration is pending.");
        }
        catch (DbUpdateException)
        {
            var resolved = await LoadAsync(payload.Id, CancellationToken.None);
            if (resolved is not null)
            {
                if (!Matches(resolved, payload)) throw OperationConflict();
                return resolved;
            }
            await ThrowStablePrepareConflictAsync(payload, CancellationToken.None);
            throw;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            var resolved = await LoadAsync(payload.Id, CancellationToken.None);
            if (resolved is not null && Matches(resolved, payload)) return resolved;
            throw;
        }
    }

    private async Task ThrowStablePrepareConflictAsync(RegistrationPayload payload, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await ActiveOperations(db.RegistrationOperations.AsNoTracking()).AnyAsync(item => item.NormalizedEmail == payload.Email, ct)) throw InProgress();
        if (await ActiveOperations(db.RegistrationOperations.AsNoTracking()).AnyAsync(item => item.Username == payload.Username, ct)) throw UsernameInProgress();
    }

    private async Task<bool> TryClaimAsync(Guid id, Guid lease, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            if (db.Database.IsRelational())
                return await db.RegistrationOperations.Where(item => item.Id == id && item.Status == RegistrationOperation.Prepared)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.CreatingIdentity)
                        .SetProperty(item => item.LeaseToken, lease).SetProperty(item => item.LeaseExpiresAt, now.Add(LeaseDuration))
                        .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1).SetProperty(item => item.UpdatedAt, now), ct) == 1;
            var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == id, ct);
            if (operation is null || operation.Status != RegistrationOperation.Prepared) return false;
            operation.Status = RegistrationOperation.CreatingIdentity;
            operation.LeaseToken = lease;
            operation.LeaseExpiresAt = now.Add(LeaseDuration);
            operation.AttemptCount++;
            operation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            try
            {
                using var resolutionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await ResolveClaimAmbiguityAsync(id, lease, resolutionTimeout.Token);
            }
            catch { }
            throw;
        }
        catch (Exception)
        {
            return await ResolveClaimAmbiguityAsync(id, lease, CancellationToken.None);
        }
    }

    private async Task<bool> ResolveClaimAmbiguityAsync(Guid id, Guid lease, CancellationToken ct)
    {
        var operation = await LoadAsync(id, ct);
        if (operation is null) return false;
        ThrowIfTerminal(operation);
        if (operation.Status == RegistrationOperation.CreatingIdentity && operation.LeaseToken == lease) return true;
        if (IsIdentityComplete(operation.Status)) return false;
        // A Prepared row is deliberately not re-claimed here: the next user retry owns a new bounded attempt.
        return false;
    }

    private async Task<RegistrationOperation?> ConfirmAsync(Guid id, Guid lease, Guid identityId, CancellationToken ct = default)
    {
        if (identityId == Guid.Empty) return null;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var changed = db.Database.IsRelational()
                ? await db.RegistrationOperations.Where(item => item.Id == id && item.Status == RegistrationOperation.CreatingIdentity && item.LeaseToken == lease)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, RegistrationOperation.IdentityConfirmed)
                        .SetProperty(item => item.IdentityId, identityId).SetProperty(item => item.LeaseToken, (Guid?)null)
                        .SetProperty(item => item.LeaseExpiresAt, (DateTimeOffset?)null).SetProperty(item => item.NextAttemptAt, (DateTimeOffset?)null)
                        .SetProperty(item => item.LastErrorCode, (string?)null).SetProperty(item => item.UpdatedAt, now), ct)
                : await ConfirmTrackedAsync(db, id, lease, identityId, now, ct);
            return changed == 1 ? await LoadAsync(id, ct) : null;
        }
        catch (OperationCanceledException)
        {
            try
            {
                using var resolutionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await ResolveConfirmationAmbiguityAsync(id, lease, identityId, resolutionTimeout.Token);
            }
            catch (Exception resolutionError)
            {
                _logger.LogWarning("Registration confirmation cancellation resolution did not complete for operation {OperationId}: {Code}.", id,
                    resolutionError is AuthException auth ? auth.Code : "resolution_failed");
            }
            throw;
        }
        catch (Exception)
        {
            return await ResolveConfirmationAmbiguityAsync(id, lease, identityId);
        }
    }

    private async Task<RegistrationOperation?> ResolveConfirmationAmbiguityAsync(Guid id, Guid lease, Guid identityId, CancellationToken ct = default)
    {
        var operation = await LoadAsync(id, ct);
        if (operation is null || operation.Status == RegistrationOperation.Prepared) throw Pending();
        ThrowIfTerminal(operation);
        if (IsIdentityComplete(operation.Status) && operation.IdentityId == identityId) return operation;
        if (operation.IdentityId is not null && operation.IdentityId != identityId)
            throw IdentityConflict();
        if (operation?.Status == RegistrationOperation.CreatingIdentity && operation.LeaseToken == lease)
        {
            await LeaveRecoverableIfOwnedAsync(id, lease, "identity_confirmation_pending", ct);
            throw Pending();
        }
        throw Pending();
    }

    private static async Task<int> ConfirmTrackedAsync(AppDbContext db, Guid id, Guid lease, Guid identityId, DateTimeOffset now, CancellationToken ct)
    {
        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (operation is null || operation.Status != RegistrationOperation.CreatingIdentity || operation.LeaseToken != lease) return 0;
        operation.Status = RegistrationOperation.IdentityConfirmed; operation.IdentityId = identityId; operation.LeaseToken = null;
        operation.LeaseExpiresAt = null; operation.NextAttemptAt = null; operation.LastErrorCode = null; operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct); return 1;
    }

    private async Task<RegistrationOperation?> RecoverAsync(RegistrationOperation? operation, Guid? lease, string code, CancellationToken ct = default)
    {
        if (operation is null) throw Pending();
        var gate = await AcquireInMemoryOperationGateAsync(operation.Id, ct);
        try { return await RecoverCoreAsync(operation, lease, code, ct); }
        finally { gate?.Release(); }
    }

    private async Task<RegistrationOperation?> RecoverCoreAsync(RegistrationOperation operation, Guid? lease, string code, CancellationToken ct = default)
    {
        // Recovery is allowed to observe without a lease, but every state mutation
        // requires the caller-owned, non-empty lease captured by its claim CAS.
        if (lease is not Guid ownedLease || ownedLease == Guid.Empty) throw Pending();
        using var lookupTimeout = ct.CanBeCanceled ? null : new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var boundedCt = ct.CanBeCanceled ? ct : lookupTimeout!.Token;
        try
        {
            var identity = await _goTrue.AdminGetUserByEmailAsync(operation.NormalizedEmail, boundedCt);
            if (identity is null)
            {
                if (!await ReturnPreparedIfOwnedAsync(operation.Id, ownedLease, code, boundedCt)) throw Pending();
                throw Pending();
            }
            var payload = new RegistrationPayload(operation.Id, operation.NormalizedEmail, operation.Username, operation.FullName);
            if (IsOwned(identity, payload))
                return await ConfirmAsync(operation.Id, ownedLease, identity.Id, boundedCt) ?? throw Pending();
            if (!await MarkConflictIfOwnedAsync(operation.Id, ownedLease, "email_already_registered", boundedCt)) throw Pending();
            throw EmailTaken();
        }
        catch (OperationCanceledException)
        {
            if (!await LeaveRecoverableIfOwnedAsync(operation.Id, ownedLease, "identity_lookup_pending", boundedCt)) throw Pending();
            throw Pending();
        }
        catch (AuthException error) when (error.Code != "email_already_registered")
        {
            if (!await LeaveRecoverableIfOwnedAsync(operation.Id, ownedLease, "identity_lookup_pending", boundedCt)) throw Pending();
            throw Pending();
        }
        catch (AuthException)
        {
            throw;
        }
        catch (Exception)
        {
            if (!await LeaveRecoverableIfOwnedAsync(operation.Id, ownedLease, "identity_lookup_pending", boundedCt)) throw Pending();
            _logger.LogWarning("Registration identity lookup is pending for operation {OperationId}.", operation.Id);
            throw Pending();
        }
    }

    private Task<bool> ReturnPreparedIfOwnedAsync(Guid id, Guid lease, string code, CancellationToken ct = default) =>
        MutateCreatingIdentityIfOwnedAsync(id, lease, operation =>
        {
            operation.Status = RegistrationOperation.Prepared; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
            operation.NextAttemptAt = null; operation.LastErrorCode = code;
        }, ct);

    private Task<bool> LeaveRecoverableIfOwnedAsync(Guid id, Guid lease, string code, CancellationToken ct = default) =>
        MutateCreatingIdentityIfOwnedAsync(id, lease, operation =>
        {
            operation.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(1); operation.LastErrorCode = code;
        }, ct);

    private Task<bool> MarkConflictIfOwnedAsync(Guid id, Guid lease, string code, CancellationToken ct = default) =>
        MutateCreatingIdentityIfOwnedAsync(id, lease, operation =>
        {
            operation.Status = RegistrationOperation.Conflict; operation.LeaseToken = null; operation.LeaseExpiresAt = null;
            operation.NextAttemptAt = null; operation.LastErrorCode = code;
        }, ct);

    private async Task<bool> MutateCreatingIdentityIfOwnedAsync(Guid id, Guid lease, Action<RegistrationOperation> mutate, CancellationToken ct)
    {
        if (lease == Guid.Empty) return false;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            // Keep the predicates in lockstep with every recovery branch: no observed
            // stale/null lease can change a newer owner's state.
            var status = RegistrationOperation.CreatingIdentity;
            var next = (DateTimeOffset?)null;
            string? error = null;
            Guid? nextLease = lease;
            DateTimeOffset? leaseExpires = null;
            var probe = new RegistrationOperation { Status = RegistrationOperation.CreatingIdentity, LeaseToken = lease };
            mutate(probe);
            status = probe.Status; next = probe.NextAttemptAt; error = probe.LastErrorCode; nextLease = probe.LeaseToken; leaseExpires = probe.LeaseExpiresAt;
            var query = db.RegistrationOperations.Where(item => item.Id == id && item.Status == RegistrationOperation.CreatingIdentity && item.LeaseToken == lease);
            if (status == RegistrationOperation.CreatingIdentity && nextLease == lease)
                return await query.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NextAttemptAt, next)
                    .SetProperty(item => item.LastErrorCode, error).SetProperty(item => item.UpdatedAt, now), ct) == 1;
            return await query.ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status).SetProperty(item => item.LeaseToken, nextLease)
                .SetProperty(item => item.LeaseExpiresAt, leaseExpires).SetProperty(item => item.NextAttemptAt, next)
                .SetProperty(item => item.LastErrorCode, error).SetProperty(item => item.UpdatedAt, now), ct) == 1;
        }

        var operation = await db.RegistrationOperations.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (operation is null || operation.Status != RegistrationOperation.CreatingIdentity || operation.LeaseToken != lease) return false;
        mutate(operation); operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<RegistrationOperation?> LoadAsync(Guid id, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operation = await db.RegistrationOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
        return operation is null ? null : Snapshot(operation);
    }

    private static Dictionary<string, object?> Metadata(RegistrationPayload payload) => new()
    {
        ["username"] = payload.Username, ["full_name"] = payload.FullName, [GoTrueMetadata.RegistrationOperationIdKey] = payload.Id.ToString(),
    };
    private static Dictionary<string, object?> Marker(Guid id) => new() { [GoTrueMetadata.RegistrationOperationIdKey] = id.ToString() };
    private static bool IsOwned(GoTrueUser user, RegistrationPayload payload) => user.Id != Guid.Empty
        && !string.IsNullOrWhiteSpace(user.Email) && string.Equals(user.Email.Trim(), payload.Email, StringComparison.OrdinalIgnoreCase)
        && GoTrueMetadata.TryGetRegistrationOperationId(user.UserMetadata, out var userId) && userId == payload.Id
        && GoTrueMetadata.TryGetRegistrationOperationId(user.AppMetadata, out var appId) && appId == payload.Id;
    private static bool Matches(RegistrationOperation operation, RegistrationPayload payload) => operation.NormalizedEmail == payload.Email && operation.Username == payload.Username && operation.FullName == payload.FullName;
    /// <summary>Shared SQL-translatable definition of an operation occupying its email/username.</summary>
    public static IQueryable<RegistrationOperation> ActiveOperations(IQueryable<RegistrationOperation> operations) => operations.Where(item =>
        item.Status != RegistrationOperation.Compensated && item.Status != RegistrationOperation.Conflict && item.Status != RegistrationOperation.Expired);
    private static void ThrowIfTerminal(RegistrationOperation operation)
    {
        if (operation.Status == RegistrationOperation.Conflict) throw Terminal(operation.LastErrorCode);
        if (operation.Status == RegistrationOperation.Compensated) throw OriginalFailure(operation);
        if (operation.Status == RegistrationOperation.Expired) throw Expired();
    }
    private static bool IsIdentityComplete(string status) => status is RegistrationOperation.IdentityConfirmed or RegistrationOperation.FinalizingProfile or RegistrationOperation.ProfileCommitted or RegistrationOperation.Completed;
    private static RegistrationOperation Snapshot(RegistrationOperation source) => new() { Id = source.Id, NormalizedEmail = source.NormalizedEmail, Username = source.Username, FullName = source.FullName, ProfileUserId = source.ProfileUserId, IdentityId = source.IdentityId, Status = source.Status, LeaseToken = source.LeaseToken, LeaseExpiresAt = source.LeaseExpiresAt, AttemptCount = source.AttemptCount, NextAttemptAt = source.NextAttemptAt, LastErrorCode = source.LastErrorCode, CreatedAt = source.CreatedAt, UpdatedAt = source.UpdatedAt, CompletedAt = source.CompletedAt };
    private static AuthException OperationConflict() => new(409, "registration_operation_conflict", "Registration operation conflicts with a different request.");
    private static AuthException InProgress() => new(409, "registration_in_progress", "Registration is already in progress for this email.");
    private static AuthException UsernameInProgress() => new(409, "username_registration_in_progress", "Registration is already in progress for this username.");
    private static AuthException UsernameTaken() => new(409, "username_taken", "Username is already taken.");
    private static AuthException EmailTaken() => new(409, "email_already_registered", "This email is already registered.");
    private static AuthException IdentityConflict() => new(409, "registration_identity_conflict", "Registration identity ownership could not be confirmed safely.");
    private static AuthException ProfileConflict() => new(409, "registration_profile_conflict", "Registration profile ownership could not be confirmed safely.");
    private static AuthException SessionUnavailable() => new(503, "registration_session_unavailable", "The account was created, but a session could not be established. Please sign in.");
    private static AuthException Terminal(string? code) => code switch
    {
        "username_taken" => UsernameTaken(),
        "role_not_seeded" => new AuthException(500, "role_not_seeded", "Student role is missing from the database."),
        "registration_profile_conflict" => ProfileConflict(),
        "email_already_registered" => EmailTaken(),
        "registration_identity_conflict" => IdentityConflict(),
        _ => new AuthException(409, "registration_conflict", "Registration cannot be completed safely."),
    };
    private static AuthException OriginalFailure(RegistrationOperation operation) => Terminal(operation.LastErrorCode);
    private static AuthException CleanupPending() => new(503, "registration_cleanup_pending", "Registration cleanup is pending. Please retry shortly.");
    private static AuthException Expired() => new(409, "registration_operation_expired", "Registration operation is closed. Start a new registration.");
    private static AuthException Pending() => new(503, "registration_pending", "Registration is pending. Please retry shortly.");
    private sealed record RegistrationPayload(Guid Id, string Email, string Username, string FullName);
}
