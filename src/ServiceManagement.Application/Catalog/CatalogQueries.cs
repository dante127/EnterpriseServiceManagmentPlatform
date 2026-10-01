using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.Application.Catalog;

/// <summary>
/// Catalog reads are the classic cache-aside candidate: they change rarely but
/// are read on nearly every ticket screen. The corresponding commands invalidate
/// the entries, so the cache never serves stale catalog data.
/// </summary>
public sealed record GetServicesQuery : IRequest<IReadOnlyList<ServiceDto>>;

public sealed class GetServicesQueryHandler : IRequestHandler<GetServicesQuery, IReadOnlyList<ServiceDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly RedisOptions _redisOptions;

    public GetServicesQueryHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redisOptions = redisOptions.Value;
    }

    public Task<IReadOnlyList<ServiceDto>> Handle(
        GetServicesQuery request,
        CancellationToken cancellationToken)
        => _cache.GetOrCreateListAsync(
            CacheKeys.Services,
            token => _dbContext.Services
                .AsNoTracking()
                .OrderBy(service => service.Name)
                .Select(service => new ServiceDto(
                    service.Id,
                    service.Name,
                    service.Code,
                    service.Description,
                    service.CategoryId,
                    service.Category.Name,
                    service.DefaultPriorityId,
                    service.DefaultSlaPolicyId,
                    service.IsActive))
                .ToListAsync(token),
            TimeSpan.FromMinutes(_redisOptions.DefaultTtlMinutes),
            cancellationToken);
}

public sealed record GetServiceCategoriesQuery : IRequest<IReadOnlyList<ServiceCategoryDto>>;

public sealed class GetServiceCategoriesQueryHandler
    : IRequestHandler<GetServiceCategoriesQuery, IReadOnlyList<ServiceCategoryDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly RedisOptions _redisOptions;

    public GetServiceCategoriesQueryHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redisOptions = redisOptions.Value;
    }

    public Task<IReadOnlyList<ServiceCategoryDto>> Handle(
        GetServiceCategoriesQuery request,
        CancellationToken cancellationToken)
        => _cache.GetOrCreateListAsync(
            CacheKeys.ServiceCategories,
            token => _dbContext.ServiceCategories
                .AsNoTracking()
                .OrderBy(category => category.Name)
                .Select(category => new ServiceCategoryDto(
                    category.Id,
                    category.Name,
                    category.Code,
                    category.Description,
                    category.ParentId,
                    category.IsActive))
                .ToListAsync(token),
            TimeSpan.FromMinutes(_redisOptions.DefaultTtlMinutes),
            cancellationToken);
}

public sealed record GetServicePrioritiesQuery : IRequest<IReadOnlyList<ServicePriorityDto>>;

public sealed class GetServicePrioritiesQueryHandler
    : IRequestHandler<GetServicePrioritiesQuery, IReadOnlyList<ServicePriorityDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly RedisOptions _redisOptions;

    public GetServicePrioritiesQueryHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redisOptions = redisOptions.Value;
    }

    public Task<IReadOnlyList<ServicePriorityDto>> Handle(
        GetServicePrioritiesQuery request,
        CancellationToken cancellationToken)
        => _cache.GetOrCreateListAsync(
            CacheKeys.ServicePriorities,
            token => _dbContext.ServicePriorities
                .AsNoTracking()
                .OrderBy(priority => priority.Level)
                .Select(priority => new ServicePriorityDto(
                    priority.Id,
                    priority.Name,
                    priority.Code,
                    priority.Level,
                    priority.ColorHex,
                    priority.IsActive))
                .ToListAsync(token),
            TimeSpan.FromMinutes(_redisOptions.DefaultTtlMinutes),
            cancellationToken);
}

public sealed record GetSlaPoliciesQuery : IRequest<IReadOnlyList<SlaPolicyDto>>;

public sealed class GetSlaPoliciesQueryHandler
    : IRequestHandler<GetSlaPoliciesQuery, IReadOnlyList<SlaPolicyDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly RedisOptions _redisOptions;

    public GetSlaPoliciesQueryHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _redisOptions = redisOptions.Value;
    }

    public Task<IReadOnlyList<SlaPolicyDto>> Handle(
        GetSlaPoliciesQuery request,
        CancellationToken cancellationToken)
        => _cache.GetOrCreateListAsync(
            CacheKeys.SlaPolicies,
            token => _dbContext.SlaPolicies
                .AsNoTracking()
                .OrderBy(policy => policy.Priority.Level)
                .Select(policy => new SlaPolicyDto(
                    policy.Id,
                    policy.Name,
                    policy.PriorityId,
                    policy.Priority.Name,
                    policy.FirstResponseMinutes,
                    policy.ResolutionMinutes,
                    policy.AtRiskThresholdPercent,
                    policy.IsActive))
                .ToListAsync(token),
            TimeSpan.FromMinutes(_redisOptions.DefaultTtlMinutes),
            cancellationToken);
}
