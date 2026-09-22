using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Users;

/// <summary>Loads a single user with the roles granted to it.</summary>
public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto>;

public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto>
{
    private readonly IApplicationDbContext _dbContext;

    public GetUserByIdQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserDto> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        // Projection only: the tracked entity never leaves the application layer.
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == request.UserId)
            .Select(candidate => new UserDto(
                candidate.Id,
                candidate.Email,
                candidate.UserName,
                candidate.FirstName,
                candidate.LastName,
                candidate.FirstName + " " + candidate.LastName,
                candidate.IsActive,
                candidate.OrganizationId,
                candidate.DepartmentId,
                candidate.CreatedAt,
                candidate.LastLoginAt,
                candidate.UserRoles.Select(userRole => userRole.Role.Name).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return user ?? throw new NotFoundException(nameof(User), request.UserId);
    }
}

/// <summary>Paged user search with e-mail/name filtering.</summary>
public sealed record SearchUsersQuery(
    string? SearchTerm,
    bool? IsActive,
    int Page = 1,
    int PageSize = PaginationRequest.DefaultPageSize) : IRequest<PagedResult<UserSummaryDto>>;

public sealed class SearchUsersQueryHandler : IRequestHandler<SearchUsersQuery, PagedResult<UserSummaryDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public SearchUsersQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<UserSummaryDto>> Handle(
        SearchUsersQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationRequest.MaxPageSize);

        var query = _dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(user =>
                user.Email.Contains(term) ||
                user.UserName.Contains(term) ||
                user.FirstName.Contains(term) ||
                user.LastName.Contains(term));
        }

        if (request.IsActive is not null)
        {
            query = query.Where(user => user.IsActive == request.IsActive);
        }

        // Count and slice in SQL: no full table is ever materialised.
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserSummaryDto(
                user.Id,
                user.FirstName + " " + user.LastName,
                user.Email,
                user.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<UserSummaryDto>(items, page, pageSize, totalCount);
    }
}
