using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Application.Common.Caching;

/// <summary>
/// Cache-aside helpers for read-mostly list projections. Every cached list goes
/// through here so the projection-to-cache cast and the factory contract are
/// written once instead of once per query handler.
/// </summary>
internal static class CacheListExtensions
{
    public static Task<IReadOnlyList<T>> GetOrCreateListAsync<T>(
        this ICacheService cache,
        string key,
        Func<CancellationToken, Task<List<T>>> load,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
        => cache.GetOrCreateAsync<IReadOnlyList<T>>(
            key,
            async token => await load(token),
            timeToLive,
            cancellationToken);
}
