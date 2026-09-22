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

    public GetTicketByIdQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TicketDetailDto> Handle(GetTicketByIdQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .AsNoTracking()
            .Where(candidate => candidate.Id == request.TicketId && !candidate.IsDeleted)
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

    public SearchTicketsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<TicketSummaryDto>> Handle(
        SearchTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationRequest.MaxPageSize);

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
        var ticketExists = await _dbContext.Tickets
            .AsNoTracking()
            .AnyAsync(ticket => ticket.Id == request.TicketId && !ticket.IsDeleted, cancellationToken);

        if (!ticketExists)
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

        var assignments = await _dbContext.TicketAssignmentHistories
            .AsNoTracking()
            .Where(history => history.TicketId == request.TicketId)
            .OrderByDescending(history => history.ChangedAt)
            .Select(history => new TicketAssignmentHistoryDto(
                history.Id,
                history.PreviousAssigneeId,
                history.PreviousAssigneeId == null
                    ? null
                    : _dbContext.Users
                        .Where(user => user.Id == history.PreviousAssigneeId)
                        .Select(user => user.FirstName + " " + user.LastName)
                        .FirstOrDefault(),
                history.NewAssigneeId,
                history.NewAssigneeId == null
                    ? null
                    : _dbContext.Users
                        .Where(user => user.Id == history.NewAssigneeId)
                        .Select(user => user.FirstName + " " + user.LastName)
                        .FirstOrDefault(),
                history.PreviousTeamId,
                history.NewTeamId,
                history.Reason,
                history.ChangedBy,
                _dbContext.Users
                    .Where(user => user.Id == history.ChangedBy)
                    .Select(user => user.FirstName + " " + user.LastName)
                    .FirstOrDefault()!,
                history.ChangedAt))
            .ToListAsync(cancellationToken);

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
