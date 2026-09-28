using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic SLA monitor. It recomputes the SLA state of open tickets, promotes
/// them to AtRisk or Breached, and raises the matching notifications.
/// <para>
/// This class owns only the schedule and the cross-replica lock. The decision
/// logic lives in <see cref="SlaCycle"/>, which is a plain scoped service and can
/// therefore be exercised directly in a test.
/// </para>
/// Safety properties:
/// <list type="bullet">
/// <item>idempotent - notifications are only produced when the state actually changes;</item>
/// <item>safe against duplicate execution - guarded by a database application lock;</item>
/// <item>retryable - a failed cycle is logged and simply retried on the next tick;</item>
/// <item>bounded - only a fixed batch of the most urgent tickets is processed per tick.</item>
/// </list>
/// </summary>
public sealed class SlaMonitorWorker : BackgroundService
{
    internal const string JobName = "sla-monitor";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackgroundJobOptions _options;
    private readonly ILogger<SlaMonitorWorker> _logger;

    public SlaMonitorWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobOptions> options,
        ILogger<SlaMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Background processing is disabled by configuration; SLA monitor will not run.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.SlaScanIntervalMinutes));
        _logger.LogInformation("SLA monitor started. Scanning every {Interval}.", interval);

        await RunSafelyAsync(stoppingToken);

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

            await RunSafelyAsync(stoppingToken);
        }
    }

    private async Task RunSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: nothing to report.
        }
        catch (Exception exception)
        {
            // Never let a failed cycle kill the worker: the next tick retries.
            _logger.LogError(exception, "SLA monitoring cycle failed and will be retried on the next tick.");
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
            // Normal in a multi-replica deployment, where every replica ticks and
            // all but one skip. Logging this per tick is pure noise.
            _logger.LogDebug("Another instance already holds the SLA monitor lock; skipping this tick.");
            return;
        }

        try
        {
            var cycle = scope.ServiceProvider.GetRequiredService<SlaCycle>();
            await cycle.RunAsync(cancellationToken);
        }
        finally
        {
            await DatabaseJobLock.ReleaseAsync(dbContext, JobName, CancellationToken.None);
        }
    }
}
