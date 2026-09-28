using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed cache configuration.</summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis configuration string, e.g. localhost:6379.</summary>
    public string Configuration { get; set; } = string.Empty;

    /// <summary>When false the API runs without a distributed cache (read-through fallback).</summary>
    public bool Enabled { get; set; } = true;

    [Range(1, 10080)]
    public int DefaultTtlMinutes { get; set; } = 10;
}
