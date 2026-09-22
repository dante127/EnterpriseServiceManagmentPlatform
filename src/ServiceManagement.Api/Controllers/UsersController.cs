using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Application.Users;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Controllers;

/// <summary>User administration. Every action is gated by a permission, never by a role name.</summary>
[ApiVersion("1.0")]
public sealed class UsersController : ApiControllerBase
{
    public UsersController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>Returns a single user.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(Guid id, CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetUserByIdQuery(id), cancellationToken));

    /// <summary>Paged user search.</summary>
    [HttpGet]
    [RequirePermission(Permissions.UsersRead)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserSummaryDto>>>> Search(
        [FromQuery] string? searchTerm,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
        => Success(await Mediator.Send(
            new SearchUsersQuery(searchTerm, isActive, page, pageSize),
            cancellationToken));

    /// <summary>Creates a user and grants the requested roles.</summary>
    [HttpPost]
    [RequirePermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create(
        [FromBody] CreateUserCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "User created.");

    /// <summary>Activates or deactivates an account.</summary>
    [HttpPut("{id:guid}/activation")]
    [RequirePermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> SetActivation(
        Guid id,
        [FromBody] SetUserActivationRequest request,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(new SetUserActivationCommand(id, request.IsActive), cancellationToken);
        return Acknowledge(request.IsActive ? "User activated." : "User deactivated.");
    }

    /// <summary>Replaces the role set of a user.</summary>
    [HttpPut("{id:guid}/roles")]
    [RequirePermission(Permissions.UsersManage)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> AssignRoles(
        Guid id,
        [FromBody] AssignUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(new AssignUserRolesCommand(id, request.Roles), cancellationToken);
        return Acknowledge("Roles updated.");
    }
}

/// <summary>Activation payload.</summary>
public sealed record SetUserActivationRequest(bool IsActive);

/// <summary>Role assignment payload.</summary>
public sealed record AssignUserRolesRequest(IReadOnlyList<string> Roles);
