using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Reports;

/// <summary>Aggregated operational picture for the dashboard screen.</summary>
public sealed record DashboardDto(
    int TotalTickets,
    int OpenTickets,
    int ResolvedLastThirtyDays,
    int BreachedSlaCount,
    int AtRiskSlaCount,
    double? AverageResolutionHours,
    IReadOnlyDictionary<string, int> TicketsByStatus,
    IReadOnlyDictionary<string, int> TicketsByPriority,
    IReadOnlyDictionary<string, int> TicketsBySlaState,
    IReadOnlyList<TicketSummaryDto> RecentTickets);

/// <summary>
/// Dashboard aggregate. Results are cached (they are read constantly and can
/// tolerate a few seconds of staleness) and every figure is computed in SQL.
/// </summary>
public sealed record GetDashboardQuery(string? RefreshKey = null) : IRequest<DashboardDto>;

public sealed class GetDashboardQueryHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private const int RecentTicketCount = 10;
    private static readonly TimeSpan DashboardCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IApplicationDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _clock;
    private readonly RedisOptions _redisOptions;

    public GetDashboardQueryHandler(
        IApplicationDbContext dbContext,
        ICacheService cache,
        IDateTimeProvider clock,
        IOptions<RedisOptions> redisOptions)
    {
        _dbContext = dbContext;
        _cache = cache;
        _clock = clock;
        _redisOptions = redisOptions.Value;
    }

    public Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
        => _cache.GetOrCreateAsync(
            $"{CacheKeys.Dashboard}:{_redisOptions.DefaultTtlMinutes}",
            BuildDashboardAsync,
            DashboardCacheTtl,
            cancellationToken);

    private async Task<DashboardDto> BuildDashboardAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);

        var tickets = _dbContext.Tickets.AsNoTracking().Where(ticket => !ticket.IsDeleted);

        var totalTickets = await tickets.CountAsync(cancellationToken);

        var openTickets = await tickets
            .CountAsync(
                ticket => ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled,
                cancellationToken);

        var resolvedLastThirtyDays = await tickets
            .CountAsync(
                ticket => ticket.ResolvedAt != null && ticket.ResolvedAt >= thirtyDaysAgo,
                cancellationToken);

        var byStatus = await tickets
            .GroupBy(ticket => ticket.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var byPriority = await tickets
            .GroupBy(ticket => ticket.Priority.Name)
            .Select(group => new { Priority = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var bySlaState = await tickets
            .GroupBy(ticket => ticket.SlaStatus)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // Only two columns are projected, and only for a bounded window, so the
        // average never drags whole rows into memory.
        var resolutionSamples = await tickets
            .Where(ticket => ticket.ResolvedAt != null && ticket.ReportedAt >= thirtyDaysAgo)
            .Select(ticket => new { ticket.ReportedAt, ResolvedAt = ticket.ResolvedAt!.Value })
            .ToListAsync(cancellationToken);

        double? averageResolutionHours = resolutionSamples.Count == 0
            ? null
            : Math.Round(
                resolutionSamples.Average(sample => (sample.ResolvedAt - sample.ReportedAt).TotalHours),
                2);

        var recentTickets = await tickets
            .OrderByDescending(ticket => ticket.ReportedAt)
            .Take(RecentTicketCount)
            .ToSummaryDto()
            .ToListAsync(cancellationToken);

        return new DashboardDto(
            totalTickets,
            openTickets,
            resolvedLastThirtyDays,
            bySlaState.FirstOrDefault(state => state.State == SlaStatus.Breached)?.Count ?? 0,
            bySlaState.FirstOrDefault(state => state.State == SlaStatus.AtRisk)?.Count ?? 0,
            averageResolutionHours,
            byStatus.ToDictionary(entry => entry.Status.ToString(), entry => entry.Count),
            byPriority.ToDictionary(entry => entry.Priority, entry => entry.Count),
            bySlaState.ToDictionary(entry => entry.State.ToString(), entry => entry.Count),
            recentTickets);
    }
}
