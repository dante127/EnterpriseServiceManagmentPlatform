using System.Text.Json;

namespace ServiceManagement.Application.Common.Auditing;

/// <summary>
/// Serialises audit payloads. Values are camel-cased and never include
/// credentials: callers are expected to pass already-safe projections.
/// </summary>
public static class AuditJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string? Serialize(object? value)
        => value is null ? null : JsonSerializer.Serialize(value, Options);
}
