using System.Text.Json;
using System.Text.Json.Nodes;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;

namespace ServiceManagement.Infrastructure.Auditing;

/// <summary>
/// Persists audit records. Payloads are passed through a redactor first so a
/// secret can never end up in the audit trail even if a caller is careless.
/// <para>
/// The record is <em>staged</em> on the caller's context, not saved here. A
/// ticket change, its notifications and its audit record must share one
/// transaction: if this service committed on its own, a later failure would leave
/// the business change applied with no audit trail, which in an audit-grade
/// system is worse than the change not happening at all.
/// </para>
/// </summary>
public sealed class AuditService : IAuditService
{
    private static readonly string[] SensitiveFragments =
    [
        "password",
        "secret",
        "token",
        "hash",
        "signingkey",
        "connectionstring"
    ];

    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditService(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Stages the record. The caller's next <c>SaveChangesAsync</c> commits it.
    /// </summary>
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        _dbContext.AuditLogs.Add(new AuditLog(
            entry.UserId ?? _currentUser.UserId,
            entry.Action,
            entry.EntityName,
            entry.EntityId,
            _clock.UtcNow,
            entry.IpAddress ?? _currentUser.IpAddress,
            entry.CorrelationId ?? _currentUser.CorrelationId,
            Redact(entry.OldValues),
            Redact(entry.NewValues)));

        return Task.CompletedTask;
    }

    /// <summary>Replaces the value of any JSON property whose name looks sensitive.</summary>
    internal static string? Redact(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return json;
            }

            RedactNode(root);
            return root.ToJsonString();
        }
        catch (JsonException)
        {
            // Not JSON: leave it untouched rather than risk mangling a value.
            return json;
        }
    }

    private static void RedactNode(JsonObject node)
    {
        foreach (var property in node.ToList())
        {
            if (property.Value is JsonObject child)
            {
                RedactNode(child);
                continue;
            }

            if (IsSensitive(property.Key))
            {
                node[property.Key] = "***redacted***";
            }
        }
    }

    private static bool IsSensitive(string propertyName)
        => SensitiveFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
