namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>
/// Small cache abstraction over the distributed cache. Implementations must
/// degrade gracefully: a cache outage may slow the API down but must never fail
/// a request.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default);
}
