using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceManagement.Api.HealthChecks;

/// <summary>Readiness probe for the distributed cache (Redis).</summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private const string ProbeKey = "health:probe";

    private readonly IDistributedCache _cache;

    public RedisHealthCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.SetStringAsync(
                ProbeKey,
                DateTimeOffset.UtcNow.ToString("O"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
                cancellationToken);

            var roundTrip = await _cache.GetStringAsync(ProbeKey, cancellationToken);

            return roundTrip is null
                ? HealthCheckResult.Degraded("The cache accepted a write but returned nothing.")
                : HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable.", exception);
        }
    }
}
