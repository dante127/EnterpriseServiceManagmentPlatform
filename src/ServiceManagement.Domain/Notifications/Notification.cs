using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Domain.Notifications;

/// <summary>
/// Persisted, per-user notification. Delivery is decoupled from the ticket
/// module: producers only know <c>INotificationService</c>.
/// </summary>
public class Notification : AuditableEntity
{
    private Notification()
    {
    }

    public Notification(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        string? entityName = null,
        string? entityId = null,
        string? correlationId = null)
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        Type = type;
        Title = Guard.NotBlank(title, nameof(title), 200);
        Message = Guard.NotBlank(message, nameof(message), 1000);
        EntityName = entityName;
        EntityId = entityId;
        CorrelationId = correlationId;
    }

    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string? EntityName { get; private set; }

    public string? EntityId { get; private set; }

    public string? CorrelationId { get; private set; }

    public bool IsRead { get; private set; }

    public User User { get; private set; } = null!;

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkAsRead(DateTimeOffset at)
    {
        IsRead = true;
        ReadAt ??= at;
    }
}
