namespace ServiceManagement.Application.Notifications;

/// <summary>Notification projection delivered to the current user.</summary>
public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Type,
    string Title,
    string Message,
    string? EntityName,
    string? EntityId,
    bool IsRead,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);
