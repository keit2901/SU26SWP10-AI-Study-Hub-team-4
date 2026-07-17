namespace AI_Study_Hub_v2.Services;

public sealed class RegistrationReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegistrationReconciliationHostedService> _logger;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;

    public RegistrationReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<RegistrationReconciliationHostedService> logger,
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _initialDelay = initialDelay ?? InitialDelay;
        _interval = interval ?? Interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(_initialDelay, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IRegistrationReconciliationService>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                _logger.LogError("Registration reconciliation background cycle failed: {Code}.",
                    error is AuthException auth ? auth.Code : "reconciliation_failed");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
