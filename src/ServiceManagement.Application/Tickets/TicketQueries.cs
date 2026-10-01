using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>Loads a single ticket with its full detail projection.</summary>
public sealed record GetTicketByIdQuery(Guid TicketId) : IRequest<TicketDetailDto>;

public sealed class GetTicketByIdQueryHandler : IRequestHandler<GetTicketByIdQuery, TicketDetailDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public GetTicketByIdQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<TicketDetailDto> Handle(GetTicketByIdQuery request, CancellationToken cancellationToken)
    {
        // Requesters are scoped to their own tickets, and a ticket they do not own
        // answers 404 rather than 403 so its existence is not revealed.
        var requesterId = TicketAccessPolicy.CanViewAllTickets(_currentUser.Permissions)
            ? (Guid?)null
            : _currentUser.RequireUserId();

        var ticket = await _dbContext.Tickets
            .AsNoTracking()
            .Where(candidate => candidate.Id == request.TicketId
                && !candidate.IsDeleted
                && (requesterId == null || candidate.RequesterId == requesterId))
            .ToDetailDto()
            .FirstOrDefaultAsync(cancellationToken);

        return ticket ?? throw new Common.Exceptions.NotFoundException(nameof(Ticket), request.TicketId);
    }
}

/// <summary>
/// Paged ticket search across every filter the UI offers. Counting, filtering,
/// ordering and slicing all happen in SQL.
/// </summary>
public sealed record SearchTicketsQuery(
    string? TicketNumber,
    string? Title,
    TicketStatus? Status,
    Guid? PriorityId,
    Guid? CategoryId,
    Guid? ServiceId,
    Guid? RequesterId,
    Guid? AssignedToId,
    Guid? AssignedTeamId,
    SlaStatus? SlaStatus,
    DateTimeOffset? ReportedFrom,
    DateTimeOffset? ReportedTo,
    int Page = 1,
    int PageSize = PaginationRequest.DefaultPageSize,
    string? SortBy = null,
    bool SortDescending = true) : IRequest<PagedResult<TicketSummaryDto>>;

public sealed class SearchTicketsQueryHandler
    : IRequestHandler<SearchTicketsQuery, PagedResult<TicketSummaryDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public SearchTicketsQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<TicketSummaryDto>> Handle(
        SearchTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationRequest.Normalize(request.Page, request.PageSize);

        var specification = new TicketQuerySpecification
        {
            TicketNumber = request.TicketNumber,
            Title = request.Title,
            Status = request.Status,
            PriorityId = request.PriorityId,
            CategoryId = request.CategoryId,
            ServiceId = request.ServiceId,
            RequesterId = request.RequesterId,
            AssignedToId = request.AssignedToId,
            AssignedTeamId = request.AssignedTeamId,
            SlaStatus = request.SlaStatus,
            ReportedFrom = request.ReportedFrom,
            ReportedTo = request.ReportedTo,
            SortBy = request.SortBy,
            SortDescending = request.SortDescending
        };

        var filtered = specification.Apply(_dbContext.Tickets.AsNoTracking());

        // Scope overrides any client-supplied filter: a caller who cannot see the
        // whole queue only ever sees the tickets they raised.
        if (!TicketAccessPolicy.CanViewAllTickets(_currentUser.Permissions))
        {
            var scopedRequesterId = _currentUser.RequireUserId();
            filtered = filtered.Where(ticket => ticket.RequesterId == scopedRequesterId);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToSummaryDto()
            .ToListAsync(cancellationToken);

        return new PagedResult<TicketSummaryDto>(items, page, pageSize, totalCount);
    }
}

/// <summary>Returns the comment and assignment trail of a ticket.</summary>
public sealed record GetTicketHistoryQuery(Guid TicketId) : IRequest<TicketHistoryDto>;

public sealed class GetTicketHistoryQueryHandler : IRequestHandler<GetTicketHistoryQuery, TicketHistoryDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public GetTicketHistoryQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<TicketHistoryDto> Handle(
        GetTicketHistoryQuery request,
        CancellationToken cancellationToken)
    {
        // Same visibility rule as detail: scoped callers get 404 for tickets they
        // did not raise, so a foreign ticket's existence is not revealed.
        var ticket = await _dbContext.Tickets
            .AsNoTracking()
            .Where(candidate => candidate.Id == request.TicketId && !candidate.IsDeleted)
            .Select(candidate => new { candidate.Id, candidate.RequesterId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null ||
            (!TicketAccessPolicy.CanViewAllTickets(_currentUser.Permissions)
                && ticket.RequesterId != _currentUser.RequireUserId()))
        {
            throw new Common.Exceptions.NotFoundException(nameof(Ticket), request.TicketId);
        }

        var canSeeInternalComments = TicketAccessPolicy.CanViewInternalComments(_currentUser.Permissions);

        var comments = await _dbContext.TicketComments
            .AsNoTracking()
            .Where(comment =>
                comment.TicketId == request.TicketId &&
                (canSeeInternalComments || !comment.IsInternal))
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new TicketCommentDto(
                comment.Id,
                comment.TicketId,
                comment.AuthorId,
                comment.Author.FirstName + " " + comment.Author.LastName,
                comment.Content,
                comment.IsInternal,
                comment.CreatedAt,
                comment.UpdatedAt))
            .ToListAsync(cancellationToken);

        // Two statements, not one per name. The trail rows are read first, then the
        // distinct user ids they mention are resolved in a single lookup. Inlining
        // the three name lookups into the projection made EF emit a correlated
        // Users subquery per name per row, so a page of history cost 3N lookups.
        var trail = await _dbContext.TicketAssignmentHistories
            .AsNoTracking()
            .Where(history => history.TicketId == request.TicketId)
            .OrderByDescending(history => history.ChangedAt)
            .Select(history => new
            {
                history.Id,
                history.PreviousAssigneeId,
                history.NewAssigneeId,
                history.PreviousTeamId,
                history.NewTeamId,
                history.Reason,
                history.ChangedBy,
                history.ChangedAt
            })
            .ToListAsync(cancellationToken);

        if (trail.Count == 0)
        {
            return new TicketHistoryDto(request.TicketId, comments, []);
        }

        // Nulls and duplicates collapse here, so a 200-row trail that mentions ten
        // people costs one query over ten ids rather than 200.
        var userIds = trail
            .SelectMany(entry => new[]
            {
                entry.PreviousAssigneeId,
                entry.NewAssigneeId,
                (Guid?)entry.ChangedBy
            })
            .Where(id => id is not null && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var namesByUserId = await _dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, Name = user.FirstName + " " + user.LastName })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Name, cancellationToken);

        string? NameOf(Guid? userId)
            => userId is not null && namesByUserId.TryGetValue(userId.Value, out var name) ? name : null;

        var assignments = trail
            .Select(entry => new TicketAssignmentHistoryDto(
                entry.Id,
                entry.PreviousAssigneeId,
                NameOf(entry.PreviousAssigneeId),
                entry.NewAssigneeId,
                NameOf(entry.NewAssigneeId),
                entry.PreviousTeamId,
                entry.NewTeamId,
                entry.Reason,
                entry.ChangedBy,
                // The acting user is required, so a name that somehow failed to
                // resolve falls back to the id rather than rendering as blank.
                NameOf(entry.ChangedBy) ?? entry.ChangedBy.ToString(),
                entry.ChangedAt))
            .ToList();

        return new TicketHistoryDto(request.TicketId, comments, assignments);
    }
}

/// <summary>Shared projection helper used by every ticket command handler.</summary>
internal static class TicketQueries
{
    public static Task<TicketDetailDto> LoadDetailAsync(
        IApplicationDbContext dbContext,
        Guid ticketId,
        CancellationToken cancellationToken)
        => dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .ToDetailDto()
            .FirstAsync(cancellationToken);
}
