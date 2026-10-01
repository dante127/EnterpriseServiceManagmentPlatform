using Microsoft.EntityFrameworkCore;
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
/// One pass of the SLA monitor: recompute the state of the tickets that are close
/// to, or past, their deadlines, and raise a notification when one changes.
/// <para>
/// This lives outside <see cref="SlaMonitorWorker"/> so the decision logic can be
/// tested without standing up a hosted service. The worker owns the schedule and
/// the cross-replica lock; the cycle owns the work.
/// </para>
/// </summary>
public sealed class SlaCycle
{
    /// <summary>
    /// A ticket is examined once its deadline is within this window, or if it has
    /// been resolved (a resolved ticket still has to be reconciled to Completed).
    /// </summary>
    private static readonly TimeSpan LookAhead = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Bounds a single pass. Without it one tick would load every overdue ticket
    /// in the system and could run long enough to overlap the next.
    /// </summary>
    private const int BatchSize = 500;

    private readonly ApplicationDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IEmailSender _emailSender;
    private readonly SlaOptions _slaOptions;
    private readonly NotificationsOptions _notificationsOptions;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SlaCycle> _logger;

    public SlaCycle(
        ApplicationDbContext dbContext,
        INotificationService notificationService,
        IEmailSender emailSender,
        IOptions<SlaOptions> slaOptions,
        IOptions<NotificationsOptions> notificationsOptions,
        IDateTimeProvider clock,
        ILogger<SlaCycle> logger)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _emailSender = emailSender;
        _slaOptions = slaOptions.Value;
        _notificationsOptions = notificationsOptions.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one monitoring pass. Never throws for an expected condition.</summary>
    public async Task<SlaCycleResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var horizon = now.Add(LookAhead);

        var atRiskByPriority = await _dbContext.SlaPolicies
            .AsNoTracking()
            .Where(policy => policy.IsActive)
            .GroupBy(policy => policy.PriorityId)
            .Select(group => new { PriorityId = group.Key, Percent = group.Min(policy => policy.AtRiskThresholdPercent) })
            .ToDictionaryAsync(entry => entry.PriorityId, entry => entry.Percent, cancellationToken);

        var candidates = await _dbContext.Tickets
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
            return SlaCycleResult.Empty;
        }

        var notifications = new List<NotificationMessage>();
        var transitions = 0;

        foreach (var ticket in candidates)
        {
            var previousState = ticket.SlaStatus;

            var atRiskThreshold = atRiskByPriority.TryGetValue(ticket.PriorityId, out var percent)
                ? percent
                : _slaOptions.AtRiskThresholdPercent;

            ticket.RefreshSlaStatus(now, atRiskThreshold);

            // Idempotence: only a real transition is worth telling anyone about.
            // Notifying every tick would spam users and grow the table without bound.
            if (ticket.SlaStatus == previousState)
            {
                continue;
            }

            transitions++;

            var isBreach = ticket.SlaStatus == SlaStatus.Breached;

            foreach (var recipient in Recipients(ticket))
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
            // One save, covering the state changes and the notification rows: the
            // notification service stages onto this context rather than flushing,
            // so a failure cannot leave a breach announced but not recorded.
            // Staging must happen before the save — a save that fails mid-way must
            // leave both the status change and the notification uncommitted, and
            // the idempotence check above would otherwise never re-raise them.
            await _notificationService.NotifyManyAsync(notifications, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (_notificationsOptions.EmailEnabled && notifications.Count > 0)
            {
                await SendEmailsAsync(notifications, cancellationToken);
            }
        }

        _logger.LogInformation(
            "SLA monitor evaluated {TicketCount} ticket(s) and recorded {TransitionCount} state change(s).",
            candidates.Count,
            transitions);

        return new SlaCycleResult(candidates.Count, transitions, notifications.Count);
    }

    /// <summary>
    /// The requester and the assignee, de-duplicated. A ticket raised and owned by
    /// the same person is common, and two identical notifications would be a small
    /// but permanent annoyance.
    /// </summary>
    private static IEnumerable<Guid> Recipients(Ticket ticket)
    {
        yield return ticket.RequesterId;

        if (ticket.AssignedToId is not null && ticket.AssignedToId.Value != ticket.RequesterId)
        {
            yield return ticket.AssignedToId.Value;
        }
    }

    private async Task SendEmailsAsync(
        IReadOnlyList<NotificationMessage> notifications,
        CancellationToken cancellationToken)
    {
        var userIds = notifications.Select(notification => notification.UserId).Distinct().ToList();

        var recipients = await _dbContext.Users
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

            // Best effort: e-mail problems must not fail the monitoring cycle, so
            // every send is isolated - one broken mailbox or a dead SMTP server
            // delays nobody else's notification and leaves the recorded state hit.
            try
            {
                await _emailSender.SendAsync(email, notification.Title, notification.Message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to send the SLA e-mail '{Subject}' to {Recipient}.",
                    notification.Title,
                    email);
            }
        }
    }
}

/// <summary>What one monitoring pass did, for logging and for tests.</summary>
/// <param name="Examined">Tickets that matched the monitoring window.</param>
/// <param name="Transitions">Tickets whose SLA state actually changed.</param>
/// <param name="Notifications">Messages queued for the affected people.</param>
public sealed record SlaCycleResult(int Examined, int Transitions, int Notifications)
{
    public static SlaCycleResult Empty => new(0, 0, 0);
}
