namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>One audit record to persist.</summary>
public sealed record AuditEntry(
    string Action,
    string EntityName,
    string EntityId,
    Guid? UserId = null,
    string? IpAddress = null,
    string? CorrelationId = null,
    string? OldValues = null,
    string? NewValues = null);

/// <summary>Writes audit records. Implementations must redact sensitive fields.</summary>
public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
