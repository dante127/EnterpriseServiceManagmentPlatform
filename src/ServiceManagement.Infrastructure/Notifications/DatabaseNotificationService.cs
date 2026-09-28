using Microsoft.Extensions.Logging;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.Infrastructure.Notifications;

/// <summary>
/// Primary notification channel: stages a durable per-user notification row that
/// the client polls. Delivery is decoupled from the producing module, so adding
/// push or SMS later means adding another implementation behind the same
/// abstraction - not touching ticket code.
/// <para>
/// Rows are staged on the caller's context rather than saved here, so a
/// notification commits together with the change that caused it. Saving
/// independently would let a notification outlive a change that was later rolled
/// back.
/// </para>
/// </summary>
public sealed class DatabaseNotificationService : INotificationService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<DatabaseNotificationService> _logger;

    public DatabaseNotificationService(
        IApplicationDbContext dbContext,
        ILogger<DatabaseNotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task NotifyAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        => NotifyManyAsync([message], cancellationToken);

    public Task NotifyManyAsync(
        IEnumerable<NotificationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var batch = messages.ToList();

        if (batch.Count == 0)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var message in batch)
        {
            _dbContext.Notifications.Add(new Notification(
                message.UserId,
                message.Type,
                message.Title,
                message.Message,
                message.EntityName,
                message.EntityId,
                message.CorrelationId));
        }

        _logger.LogInformation(
            "Queued {NotificationCount} notification(s) of type(s) {NotificationTypes} for the current unit of work.",
            batch.Count,
            string.Join(",", batch.Select(message => message.Type).Distinct()));

        return Task.CompletedTask;
    }
}
