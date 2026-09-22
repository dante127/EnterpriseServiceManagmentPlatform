using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.Infrastructure.Caching;

/// <summary>
/// Cache-aside wrapper over <see cref="IDistributedCache"/>. Every operation is
/// best-effort: if Redis is unavailable the API keeps working (slower), because
/// a cache outage must never take tickets down.
/// </summary>
public sealed class DistributedCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly RedisOptions _options;
    private readonly ILogger<DistributedCacheService> _logger;

    public DistributedCacheService(
        IDistributedCache cache,
        IOptions<RedisOptions> options,
        ILogger<DistributedCacheService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return default;
        }

        try
        {
            var payload = await _cache.GetAsync(key, cancellationToken);

            return payload is null or { Length: 0 }
                ? default
                : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Cache read failed for {CacheKey}; serving from source.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || value is null)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);

            await _cache.SetAsync(
                key,
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = timeToLive
                        ?? TimeSpan.FromMinutes(Math.Max(1, _options.DefaultTtlMinutes))
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Cache write failed for {CacheKey}.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Cache invalidation failed for {CacheKey}.", key);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await GetAsync<T>(key, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var created = await factory(cancellationToken);

        await SetAsync(key, created, timeToLive, cancellationToken);

        return created;
    }
}
