using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Auditing;

/// <summary>
/// Immutable audit record. Old/new values are stored as JSON snapshots that are
/// produced by a redaction-aware serializer, so secrets never reach this table.
/// </summary>
public class AuditLog : BaseEntity
{
    private AuditLog()
    {
    }

    public AuditLog(
        Guid? userId,
        string action,
        string entityName,
        string entityId,
        DateTimeOffset occurredAt,
        string? ipAddress = null,
        string? correlationId = null,
        string? oldValues = null,
        string? newValues = null)
    {
        UserId = userId;
        Action = Guard.NotBlank(action, nameof(action), 100);
        EntityName = Guard.NotBlank(entityName, nameof(entityName), 200);
        EntityId = Guard.NotBlank(entityId, nameof(entityId), 128);
        OccurredAt = occurredAt;
        IpAddress = ipAddress;
        CorrelationId = correlationId;
        OldValues = oldValues;
        NewValues = newValues;
    }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityName { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public string? IpAddress { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }
}
