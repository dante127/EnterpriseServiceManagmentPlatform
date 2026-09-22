using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Domain.Auditing;

namespace ServiceManagement.Application.Audit;

/// <summary>Audit log projection.</summary>
public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string? UserName,
    string Action,
    string EntityName,
    string EntityId,
    DateTimeOffset OccurredAt,
    string? IpAddress,
    string? CorrelationId,
    string? OldValues,
    string? NewValues);

/// <summary>Paged audit trail search. Read-only and never cached (compliance data).</summary>
public sealed record GetAuditLogsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Action,
    string? EntityName,
    Guid? UserId,
    int Page = 1,
    int PageSize = PaginationRequest.DefaultPageSize) : IRequest<PagedResult<AuditLogDto>>;

public sealed class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetAuditLogsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AuditLogDto>> Handle(
        GetAuditLogsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationRequest.MaxPageSize);

        var query = _dbContext.AuditLogs.AsNoTracking();

        if (request.From is not null)
        {
            query = query.Where(entry => entry.OccurredAt >= request.From);
        }

        if (request.To is not null)
        {
            query = query.Where(entry => entry.OccurredAt <= request.To);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(entry => entry.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityName))
        {
            var entityName = request.EntityName.Trim();
            query = query.Where(entry => entry.EntityName == entityName);
        }

        if (request.UserId is not null)
        {
            query = query.Where(entry => entry.UserId == request.UserId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(entry => entry.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new AuditLogDto(
                entry.Id,
                entry.UserId,
                entry.UserId == null
                    ? null
                    : _dbContext.Users
                        .Where(user => user.Id == entry.UserId)
                        .Select(user => user.FirstName + " " + user.LastName)
                        .FirstOrDefault(),
                entry.Action,
                entry.EntityName,
                entry.EntityId,
                entry.OccurredAt,
                entry.IpAddress,
                entry.CorrelationId,
                entry.OldValues,
                entry.NewValues))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, page, pageSize, totalCount);
    }
}

/// <summary>Distinct audit actions, used to populate filter pickers.</summary>
public sealed record GetAuditActionsQuery : IRequest<IReadOnlyList<string>>;

public sealed class GetAuditActionsQueryHandler : IRequestHandler<GetAuditActionsQuery, IReadOnlyList<string>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetAuditActionsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<string>> Handle(
        GetAuditActionsQuery request,
        CancellationToken cancellationToken)
        => await _dbContext.AuditLogs
            .AsNoTracking()
            .Select(entry => entry.Action)
            .Distinct()
            .OrderBy(action => action)
            .ToListAsync(cancellationToken);
}
