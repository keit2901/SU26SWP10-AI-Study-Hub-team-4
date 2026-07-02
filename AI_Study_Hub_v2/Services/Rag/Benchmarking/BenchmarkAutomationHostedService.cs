using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed class BenchmarkAutomationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<RagOptions> _ragOptions;
    private readonly IOptionsMonitor<GroqOptions> _groqOptions;
    private readonly ILogger<BenchmarkAutomationHostedService> _logger;

    public BenchmarkAutomationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<RagOptions> ragOptions,
        IOptionsMonitor<GroqOptions> groqOptions,
        ILogger<BenchmarkAutomationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _ragOptions = ragOptions;
        _groqOptions = groqOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_ragOptions.CurrentValue.BenchmarkAutomationEnabled)
        {
            _logger.LogInformation("Benchmark automation is disabled.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _ragOptions.CurrentValue.BenchmarkAutomationIntervalHours));
        _logger.LogInformation("Benchmark automation started with interval {IntervalHours}h.", interval.TotalHours);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automated benchmark run failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<BenchmarkRunner>();

        var admin = await db.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .Where(x => x.IsActive && x.Role.RoleName == Role.AdminRoleName)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (admin is null)
        {
            _logger.LogWarning("Skipping automated benchmark: no active admin profile found.");
            return;
        }

        await runner.RunAsync(
            admin.SupabaseUserId,
            new BenchmarkConfig(
                _groqOptions.CurrentValue.Model,
                Count: null,
                DocumentIds: null,
                IsAutomated: true),
            cancellationToken: cancellationToken);
    }
}
