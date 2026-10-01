using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Application.Organizations;

/// <summary>Paged team search, optionally scoped to an organization.</summary>
public sealed record SearchTeamsQuery(
    Guid? OrganizationId,
    string? SearchTerm,
    int Page = 1,
    int PageSize = PaginationRequest.DefaultPageSize) : IRequest<PagedResult<TeamDto>>;

public sealed class SearchTeamsQueryHandler : IRequestHandler<SearchTeamsQuery, PagedResult<TeamDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public SearchTeamsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<TeamDto>> Handle(SearchTeamsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationRequest.Normalize(request.Page, request.PageSize);

        var query = _dbContext.Teams.AsNoTracking();

        if (request.OrganizationId is not null)
        {
            query = query.Where(team => team.OrganizationId == request.OrganizationId);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.Trim();
            query = query.Where(team => team.Name.Contains(term) || team.Code.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Single round trip: the aggregate is projected directly in SQL.
        var items = await query
            .OrderBy(team => team.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(team => new TeamDto(
                team.Id,
                team.OrganizationId,
                team.DepartmentId,
                team.Name,
                team.Code,
                team.Description,
                team.ManagerId,
                team.Manager == null ? null : team.Manager.FirstName + " " + team.Manager.LastName,
                team.Members.Count))
            .ToListAsync(cancellationToken);

        return new PagedResult<TeamDto>(items, page, pageSize, totalCount);
    }
}

/// <summary>Lists the members of a team.</summary>
public sealed record GetTeamMembersQuery(Guid TeamId) : IRequest<IReadOnlyList<TeamMemberDto>>;

public sealed class GetTeamMembersQueryHandler
    : IRequestHandler<GetTeamMembersQuery, IReadOnlyList<TeamMemberDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetTeamMembersQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TeamMemberDto>> Handle(
        GetTeamMembersQuery request,
        CancellationToken cancellationToken)
    {
        var teamExists = await _dbContext.Teams
            .AnyAsync(team => team.Id == request.TeamId, cancellationToken);

        if (!teamExists)
        {
            throw new NotFoundException(nameof(Team), request.TeamId);
        }

        return await _dbContext.TeamMembers
            .AsNoTracking()
            .Where(member => member.TeamId == request.TeamId)
            .OrderByDescending(member => member.IsManager)
            .ThenBy(member => member.User.LastName)
            .Select(member => new TeamMemberDto(
                member.UserId,
                member.User.FirstName + " " + member.User.LastName,
                member.User.Email,
                member.IsManager,
                member.JoinedAt))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Lists organizations.</summary>
public sealed record GetOrganizationsQuery : IRequest<IReadOnlyList<OrganizationDto>>;

public sealed class GetOrganizationsQueryHandler
    : IRequestHandler<GetOrganizationsQuery, IReadOnlyList<OrganizationDto>>
{
    private readonly IApplicationDbContext _dbContext;

    public GetOrganizationsQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OrganizationDto>> Handle(
        GetOrganizationsQuery request,
        CancellationToken cancellationToken)
        => await _dbContext.Organizations
            .AsNoTracking()
            .OrderBy(organization => organization.Name)
            .Select(organization => new OrganizationDto(
                organization.Id,
                organization.Name,
                organization.Code,
                organization.Description,
                organization.IsActive,
                organization.Departments.Count,
                organization.Teams.Count))
            .ToListAsync(cancellationToken);
}
