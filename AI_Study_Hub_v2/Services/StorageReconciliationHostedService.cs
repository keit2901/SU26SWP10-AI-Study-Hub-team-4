namespace AI_Study_Hub_v2.Services;

public sealed class StorageReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorageReconciliationHostedService> _logger;

    public StorageReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<StorageReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), ct);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var reconciliation = scope.ServiceProvider.GetRequiredService<IStorageReconciliationService>();
                await reconciliation.ReconcileAllAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storage reconciliation background job failed.");
                // Do not rethrow — keep the app alive.
            }
        }
    }
}
