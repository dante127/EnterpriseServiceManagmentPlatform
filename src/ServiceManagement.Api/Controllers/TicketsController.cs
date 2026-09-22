using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Constants;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Api.Controllers;

/// <summary>Ticket lifecycle: create, search, update, assign, move status, comment and close.</summary>
[ApiVersion("1.0")]
public sealed class TicketsController : ApiControllerBase
{
    public TicketsController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>
    /// Searches tickets. Every filter is applied in SQL and the result is paged server-side.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.TicketsRead)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TicketSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TicketSummaryDto>>>> Search(
        [FromQuery] string? ticketNumber,
        [FromQuery] string? title,
        [FromQuery] TicketStatus? status,
        [FromQuery] Guid? priorityId,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? serviceId,
        [FromQuery] Guid? requesterId,
        [FromQuery] Guid? assignedToId,
        [FromQuery] Guid? assignedTeamId,
        [FromQuery] SlaStatus? slaStatus,
        [FromQuery] DateTimeOffset? reportedFrom,
        [FromQuery] DateTimeOffset? reportedTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default)
        => Success(await Mediator.Send(
            new SearchTicketsQuery(
                ticketNumber,
                title,
                status,
                priorityId,
                categoryId,
                serviceId,
                requesterId,
                assignedToId,
                assignedTeamId,
                slaStatus,
                reportedFrom,
                reportedTo,
                page,
                pageSize,
                sortBy,
                sortDescending),
            cancellationToken));

    /// <summary>Returns one ticket.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.TicketsRead)]
    [ProducesResponseType(typeof(ApiResponse<TicketDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TicketDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetTicketByIdQuery(id), cancellationToken));

    /// <summary>Returns the comment and assignment trail of a ticket.</summary>
    [HttpGet("{id:guid}/history")]
    [RequirePermission(Permissions.TicketsRead)]
    [ProducesResponseType(typeof(ApiResponse<TicketHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TicketHistoryDto>>> GetHistory(
        Guid id,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetTicketHistoryQuery(id), cancellationToken));

    /// <summary>
    /// Raises a ticket. Send an <c>idempotencyKey</c> to make network retries safe:
    /// a repeated call returns the ticket created by the first one.
    /// </summary>
    [HttpPost]
    [RequirePermission(Permissions.TicketsCreate)]
    [ProducesResponseType(typeof(ApiResponse<TicketDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TicketDetailDto>>> Create(
        [FromBody] CreateTicketCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Ticket created.");

    /// <summary>Updates ticket details and, optionally, its priority (SLA targets are recalculated).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.TicketsUpdate)]
    [ProducesResponseType(typeof(ApiResponse<TicketDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TicketDetailDto>>> Update(
        Guid id,
        [FromBody] UpdateTicketRequest request,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(
            new UpdateTicketCommand(id, request.Title, request.Description, request.CategoryId, request.PriorityId, request.RowVersion),
            cancellationToken));

    /// <summary>Assigns or reassigns the ticket to a user and/or a team.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequirePermission(Permissions.TicketsAssign)]
    [ProducesResponseType(typeof(ApiResponse<TicketDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TicketDetailDto>>> Assign(
        Guid id,
        [FromBody] AssignTicketRequest request,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(
            new AssignTicketCommand(id, request.AssigneeId, request.TeamId, request.Reason, request.RowVersion),
            cancellationToken));

    /// <summary>Moves the ticket through the controlled workflow.</summary>
    [HttpPost("{id:guid}/status")]
    [RequirePermission(Permissions.TicketsUpdate)]
    [ProducesResponseType(typeof(ApiResponse<TicketDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TicketDetailDto>>> ChangeStatus(
        Guid id,
        [FromBody] ChangeTicketStatusRequest request,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(
            new ChangeTicketStatusCommand(id, request.Status, request.RowVersion),
            cancellationToken));

    /// <summary>Adds a comment. Internal comments require the tickets.comment.internal permission.</summary>
    [HttpPost("{id:guid}/comments")]
    [RequirePermission(Permissions.TicketsComment)]
    [ProducesResponseType(typeof(ApiResponse<TicketCommentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TicketCommentDto>>> AddComment(
        Guid id,
        [FromBody] AddTicketCommentRequest request,
        CancellationToken cancellationToken)
        => Created(
            await Mediator.Send(new AddTicketCommentCommand(id, request.Content, request.IsInternal), cancellationToken),
            "Comment added.");

    /// <summary>Soft-deletes a ticket. The row is retained for auditing.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.TicketsDelete)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        await Mediator.Send(new DeleteTicketCommand(id), cancellationToken);
        return Acknowledge("Ticket deleted.");
    }
}

/// <summary>Ticket update payload.</summary>
public sealed record UpdateTicketRequest(
    string Title,
    string Description,
    Guid CategoryId,
    Guid? PriorityId,
    string? RowVersion);

/// <summary>Assignment payload.</summary>
public sealed record AssignTicketRequest(
    Guid? AssigneeId,
    Guid? TeamId,
    string? Reason,
    string? RowVersion);

/// <summary>Status transition payload.</summary>
public sealed record ChangeTicketStatusRequest(TicketStatus Status, string? RowVersion);

/// <summary>Comment payload.</summary>
public sealed record AddTicketCommentRequest(string Content, bool IsInternal);
