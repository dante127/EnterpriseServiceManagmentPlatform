using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Application.Organizations;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Controllers;

/// <summary>Organizations, departments and teams.</summary>
[ApiVersion("1.0")]
public sealed class OrganizationsController : ApiControllerBase
{
    public OrganizationsController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>Lists organizations.</summary>
    [HttpGet]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OrganizationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrganizationDto>>>> GetAll(
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetOrganizationsQuery(), cancellationToken));

    /// <summary>Creates an organization.</summary>
    [HttpPost]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<OrganizationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<OrganizationDto>>> Create(
        [FromBody] CreateOrganizationCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Organization created.");

    /// <summary>Creates a department inside an organization.</summary>
    [HttpPost("departments")]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> CreateDepartment(
        [FromBody] CreateDepartmentCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Department created.");

    /// <summary>Creates a team inside a department.</summary>
    [HttpPost("teams")]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<TeamDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TeamDto>>> CreateTeam(
        [FromBody] CreateTeamCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Team created.");

    /// <summary>Paged team search.</summary>
    [HttpGet("teams")]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TeamDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TeamDto>>>> SearchTeams(
        [FromQuery] Guid? organizationId,
        [FromQuery] string? searchTerm,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
        => Success(await Mediator.Send(
            new SearchTeamsQuery(organizationId, searchTerm, page, pageSize),
            cancellationToken));

    /// <summary>Lists the members of a team.</summary>
    [HttpGet("teams/{teamId:guid}/members")]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeamMemberDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TeamMemberDto>>>> GetMembers(
        Guid teamId,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetTeamMembersQuery(teamId), cancellationToken));

    /// <summary>Adds a user to a team.</summary>
    [HttpPost("teams/{teamId:guid}/members")]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<TeamMemberDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TeamMemberDto>>> AddMember(
        Guid teamId,
        [FromBody] AddTeamMemberRequest request,
        CancellationToken cancellationToken)
        => Created(
            await Mediator.Send(new AddTeamMemberCommand(teamId, request.UserId, request.IsManager), cancellationToken),
            "Team member added.");

    /// <summary>Removes a user from a team.</summary>
    [HttpDelete("teams/{teamId:guid}/members/{userId:guid}")]
    [RequirePermission(Permissions.TeamsManage)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> RemoveMember(
        Guid teamId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(new RemoveTeamMemberCommand(teamId, userId), cancellationToken);
        return Acknowledge("Team member removed.");
    }
}

/// <summary>Team membership payload.</summary>
public sealed record AddTeamMemberRequest(Guid UserId, bool IsManager);
