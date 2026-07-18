using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

/// <summary>Runs bounded durable-registration recovery and retention work.</summary>
public sealed class RegistrationReconciliationService(
    IServiceScopeFactory scopeFactory,
    IRegistrationCoordinator coordinator,
    ILogger<RegistrationReconciliationService> logger) : IRegistrationReconciliationService
{
    private const int BatchSize = 25;

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidateIds = await db.RegistrationOperations.AsNoTracking()
            .Where(operation =>
                operation.Status == RegistrationOperation.IdentityConfirmed && (operation.NextAttemptAt == null || operation.NextAttemptAt <= now)
                || operation.Status == RegistrationOperation.CompensationRequired && (operation.NextAttemptAt == null || operation.NextAttemptAt <= now)
                || operation.Status == RegistrationOperation.Prepared && operation.UpdatedAt <= now.AddHours(-24)
                || operation.Status == RegistrationOperation.CreatingIdentity && (operation.LeaseToken == null || operation.LeaseExpiresAt == null || operation.LeaseExpiresAt <= now)
                || operation.Status == RegistrationOperation.FinalizingProfile && (operation.LeaseToken == null || operation.LeaseExpiresAt == null || operation.LeaseExpiresAt <= now)
                || operation.Status == RegistrationOperation.Compensating && (operation.LeaseToken == null || operation.LeaseExpiresAt == null || operation.LeaseExpiresAt <= now))
            .OrderBy(operation => operation.NextAttemptAt ?? operation.UpdatedAt)
            .ThenBy(operation => operation.UpdatedAt).ThenBy(operation => operation.Id)
            .Select(operation => operation.Id).Take(BatchSize).ToListAsync(cancellationToken);

        foreach (var operationId in candidateIds)
        {
            try { await coordinator.ReconcileAsync(operationId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning("Registration reconciliation operation {OperationId} failed: {Code}.", operationId,
                    error is AuthException auth ? auth.Code : "reconciliation_failed");
            }
        }

        var cutoff = now.AddDays(-30);
        var terminalIds = await db.RegistrationOperations
            .Where(operation => (operation.Status == RegistrationOperation.Completed
                    || operation.Status == RegistrationOperation.ProfileCommitted
                    || operation.Status == RegistrationOperation.Compensated
                    || operation.Status == RegistrationOperation.Conflict
                    || operation.Status == RegistrationOperation.Expired)
                && (operation.CompletedAt ?? operation.UpdatedAt) <= cutoff)
            .OrderBy(operation => operation.CompletedAt ?? operation.UpdatedAt).ThenBy(operation => operation.Id)
            .Select(operation => operation.Id).Take(BatchSize).ToListAsync(cancellationToken);
        if (terminalIds.Count == 0) return;

        if (db.Database.IsRelational())
        {
            await db.RegistrationOperations.Where(operation => terminalIds.Contains(operation.Id)).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var terminalRows = await db.RegistrationOperations.Where(operation => terminalIds.Contains(operation.Id)).ToListAsync(cancellationToken);
        db.RegistrationOperations.RemoveRange(terminalRows);
        await db.SaveChangesAsync(cancellationToken);
    }
}
