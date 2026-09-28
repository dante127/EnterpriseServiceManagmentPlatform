using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Infrastructure.BackgroundJobs;

/// <summary>
/// One pass of the maintenance job: purges expired refresh tokens and old
/// read notifications.
/// <para>
/// Split out from <see cref="MaintenanceWorker"/> so the retention rules can be
/// tested without standing up a hosted service. The worker owns the schedule and
/// the cross-replica lock; the cycle owns the work.
/// </para>
/// </summary>
public sealed class MaintenanceCycle
{
    /// <summary>
    /// How long an expired or revoked refresh token is kept after it lapses. The
    /// delay exists so a token revoked in the same request that used it can still
    /// be recognised during a replay window.
    /// </summary>
    private const int RetainedDaysAfterExpiry = 7;

    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationsOptions _notificationsOptions;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<MaintenanceCycle> _logger;

    public MaintenanceCycle(
        ApplicationDbContext dbContext,
        IOptions<NotificationsOptions> notificationsOptions,
        IDateTimeProvider clock,
        ILogger<MaintenanceCycle> logger)
    {
        _dbContext = dbContext;
        _notificationsOptions = notificationsOptions.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs one maintenance pass and reports what it removed.</summary>
    public async Task<MaintenanceCycleResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var tokenCutoff = now.AddDays(-RetainedDaysAfterExpiry);

        // Set-based deletes: a user with thousands of stale tokens must not be
        // materialised into the change tracker just to delete them.
        var removedTokens = await _dbContext.RefreshTokens
            .Where(token =>
                token.ExpiresAt < tokenCutoff
                || (token.RevokedAt != null && token.RevokedAt < tokenCutoff))
            .ExecuteDeleteAsync(cancellationToken);

        var notificationCutoff = now.AddDays(-Math.Max(1, _notificationsOptions.ReadNotificationRetentionDays));

        var removedNotifications = await _dbContext.Notifications
            .Where(notification => notification.IsRead && notification.CreatedAt < notificationCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation(
            "Maintenance removed {RemovedTokens} expired refresh token(s) and {RemovedNotifications} old notification(s).",
            removedTokens,
            removedNotifications);

        return new MaintenanceCycleResult(removedTokens, removedNotifications);
    }
}

/// <summary>What one maintenance pass removed, for logging and for tests.</summary>
public sealed record MaintenanceCycleResult(int RemovedRefreshTokens, int RemovedNotifications);
