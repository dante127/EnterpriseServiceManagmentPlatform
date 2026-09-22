using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>Payload of a notification to be delivered.</summary>
public sealed record NotificationMessage(
    Guid UserId,
    NotificationType Type,
    string Title,
    string Message,
    string? EntityName = null,
    string? EntityId = null,
    string? CorrelationId = null);

/// <summary>
/// Notification abstraction. The ticket module depends only on this, so adding
/// an e-mail, push or webhook channel never touches business code.
/// </summary>
public interface INotificationService
{
    Task NotifyAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    Task NotifyManyAsync(IEnumerable<NotificationMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>Outbound e-mail channel. The portfolio build ships a logging implementation.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
