using System.Text.Json;
using System.Text.Json.Nodes;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;

namespace ServiceManagement.Infrastructure.Auditing;

/// <summary>
/// Persists audit records. Payloads are passed through a redactor first so a
/// secret can never end up in the audit trail even if a caller is careless.
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

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

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

        await _dbContext.SaveChangesAsync(cancellationToken);
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
