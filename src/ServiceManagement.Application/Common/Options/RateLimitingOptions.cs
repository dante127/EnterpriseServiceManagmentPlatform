using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed rate-limit configuration.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Ceiling for every endpoint, per client IP. The authentication endpoints
    /// carry their own tighter limit; this one bounds everything else, including
    /// the write endpoints a compromised client could hammer.
    /// </summary>
    [Range(1, 100_000)]
    public int GlobalPermitLimit { get; set; } = 300;

    [Range(1, 3600)]
    public int GlobalWindowSeconds { get; set; } = 60;

    /// <summary>Login and refresh attempts per client IP per window.</summary>
    [Range(1, 1000)]
    public int AuthenticationPermitLimit { get; set; } = 10;

    [Range(1, 3600)]
    public int AuthenticationWindowSeconds { get; set; } = 60;
}
