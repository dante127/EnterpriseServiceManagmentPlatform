using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Application.Notifications;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Controllers;

/// <summary>Inbox of the authenticated user.</summary>
[ApiVersion("1.0")]
public sealed class NotificationsController : ApiControllerBase
{
    public NotificationsController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>Lists the notifications of the caller.</summary>
    [HttpGet]
    [RequirePermission(Permissions.NotificationsRead)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NotificationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> GetMine(
        [FromQuery] bool? isRead,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
        => Success(await Mediator.Send(new GetNotificationsQuery(isRead, page, pageSize), cancellationToken));

    /// <summary>Marks one notification as read.</summary>
    [HttpPost("{id:guid}/read")]
    [RequirePermission(Permissions.NotificationsRead)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object>>> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        await Mediator.Send(new MarkNotificationReadCommand(id), cancellationToken);
        return Acknowledge("Notification marked as read.");
    }

    /// <summary>Marks every notification of the caller as read.</summary>
    [HttpPost("read-all")]
    [RequirePermission(Permissions.NotificationsRead)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var updated = await Mediator.Send(new MarkAllNotificationsReadCommand(), cancellationToken);
        return Success(updated, $"{updated} notification(s) marked as read.");
    }
}
