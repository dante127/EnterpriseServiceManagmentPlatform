using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic housekeeping: purges expired/revoked refresh tokens and old read
/// notifications. Both operations are naturally idempotent, and the cycle is
/// guarded by a database application lock so replicas cannot double up.
/// </summary>
public sealed class MaintenanceWorker : BackgroundService
{
    internal const string JobName = "maintenance";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackgroundJobOptions _options;
    private readonly ILogger<MaintenanceWorker> _logger;

    public MaintenanceWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobOptions> options,
        ILogger<MaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Background processing is disabled by configuration; maintenance worker will not run.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, _options.RefreshTokenCleanupIntervalMinutes));
        _logger.LogInformation("Maintenance worker started. Running every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool ticked;

            try
            {
                ticked = await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!ticked)
            {
                break;
            }

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Maintenance cycle failed and will be retried on the next tick.");
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // The lock is taken on the raw context, before the cycle resolves its own
        // dependencies: it must cover the whole pass, and a second replica that
        // cannot take the lock should do no work at all.
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var lockTaken = await DatabaseJobLock.TryAcquireAsync(dbContext, JobName, cancellationToken);

        if (!lockTaken)
        {
            _logger.LogDebug("Another instance already holds the maintenance lock; skipping this tick.");
            return;
        }

        try
        {
            var cycle = scope.ServiceProvider.GetRequiredService<MaintenanceCycle>();
            await cycle.RunAsync(cancellationToken);
        }
        finally
        {
            await DatabaseJobLock.ReleaseAsync(dbContext, JobName, CancellationToken.None);
        }
    }
}
