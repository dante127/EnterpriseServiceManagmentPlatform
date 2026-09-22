using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic SLA monitor. It recomputes the SLA state of open tickets, promotes
/// them to AtRisk or Breached, and raises the matching notifications.
///
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

    private const int BatchSize = 500;

    private static readonly TimeSpan LookAhead = TimeSpan.FromMinutes(60);

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
        var services = scope.ServiceProvider;

        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var notificationService = services.GetRequiredService<INotificationService>();
        var emailSender = services.GetRequiredService<IEmailSender>();
        var slaOptions = services.GetRequiredService<IOptions<SlaOptions>>().Value;
        var notificationsOptions = services.GetRequiredService<IOptions<NotificationsOptions>>().Value;

        var lockTaken = await DatabaseJobLock.TryAcquireAsync(dbContext, JobName, cancellationToken);

        if (!lockTaken)
        {
            _logger.LogInformation("Another instance already holds the SLA monitor lock; skipping this tick.");
            return;
        }

        try
        {
            var now = clock.UtcNow;
            var horizon = now.Add(LookAhead);

            var atRiskByPriority = await dbContext.SlaPolicies
                .AsNoTracking()
                .Where(policy => policy.IsActive)
                .GroupBy(policy => policy.PriorityId)
                .Select(group => new { PriorityId = group.Key, Percent = group.Min(policy => policy.AtRiskThresholdPercent) })
                .ToDictionaryAsync(entry => entry.PriorityId, entry => entry.Percent, cancellationToken);

            var candidates = await dbContext.Tickets
                .Where(ticket =>
                    !ticket.IsDeleted
                    && ticket.ResolutionDueAt != null
                    && ticket.SlaStatus != SlaStatus.Completed
                    && (ticket.ResolutionDueAt <= horizon
                        || ticket.FirstResponseDueAt <= horizon
                        || ticket.ResolvedAt != null))
                .OrderBy(ticket => ticket.ResolutionDueAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return;
            }

            var notifications = new List<NotificationMessage>();
            var transitions = 0;

            foreach (var ticket in candidates)
            {
                var previousState = ticket.SlaStatus;

                var atRiskThreshold = atRiskByPriority.TryGetValue(ticket.PriorityId, out var percent)
                    ? percent
                    : slaOptions.AtRiskThresholdPercent;

                ticket.RefreshSlaStatus(now, atRiskThreshold);

                if (ticket.SlaStatus == previousState)
                {
                    continue;
                }

                transitions++;

                var recipients = new List<Guid> { ticket.RequesterId };

                if (ticket.AssignedToId is not null)
                {
                    recipients.Add(ticket.AssignedToId.Value);
                }

                var isBreach = ticket.SlaStatus == SlaStatus.Breached;

                foreach (var recipient in recipients.Distinct())
                {
                    notifications.Add(new NotificationMessage(
                        recipient,
                        isBreach ? NotificationType.SlaBreached : NotificationType.SlaNearBreach,
                        isBreach ? "SLA breached" : "SLA at risk",
                        isBreach
                            ? $"Ticket {ticket.TicketNumber} '{ticket.Title}' has breached its SLA target."
                            : $"Ticket {ticket.TicketNumber} '{ticket.Title}' is approaching its SLA deadline.",
                        nameof(Ticket),
                        ticket.Id.ToString()));
                }
            }

            if (transitions > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await notificationService.NotifyManyAsync(notifications, cancellationToken);

                if (notificationsOptions.EmailEnabled && notifications.Count > 0)
                {
                    await SendEmailsAsync(dbContext, emailSender, notifications, cancellationToken);
                }
            }

            _logger.LogInformation(
                "SLA monitor evaluated {TicketCount} ticket(s) and recorded {TransitionCount} state change(s).",
                candidates.Count,
                transitions);
        }
        finally
        {
            await DatabaseJobLock.ReleaseAsync(dbContext, JobName, CancellationToken.None);
        }
    }

    private static async Task SendEmailsAsync(
        ApplicationDbContext dbContext,
        IEmailSender emailSender,
        IReadOnlyList<NotificationMessage> notifications,
        CancellationToken cancellationToken)
    {
        var userIds = notifications.Select(notification => notification.UserId).Distinct().ToList();

        var recipients = await dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id) && user.IsActive)
            .Select(user => new { user.Id, user.Email })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Email, cancellationToken);

        foreach (var notification in notifications)
        {
            if (!recipients.TryGetValue(notification.UserId, out var email) || string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            // Best effort: e-mail problems must not fail the monitoring cycle.
            await emailSender.SendAsync(email, notification.Title, notification.Message, cancellationToken);
        }
    }
}
