using Microsoft.Extensions.Logging;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.Infrastructure.Notifications;

/// <summary>
/// Primary notification channel: writes a durable per-user notification row that
/// the client polls. Delivery is decoupled from the producing module, so adding
/// push or SMS later means adding another implementation behind the same
/// abstraction - not touching ticket code.
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

    public async Task NotifyManyAsync(
        IEnumerable<NotificationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var batch = messages.ToList();

        if (batch.Count == 0)
        {
            return;
        }

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stored {NotificationCount} notification(s) of type(s) {NotificationTypes}.",
            batch.Count,
            string.Join(",", batch.Select(message => message.Type).Distinct()));
    }
}
