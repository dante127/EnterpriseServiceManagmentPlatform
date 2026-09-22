using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Audit;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Controllers;

/// <summary>Read access to the audit trail.</summary>
[ApiVersion("1.0")]
public sealed class AuditController : ApiControllerBase
{
    public AuditController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>Paged audit search.</summary>
    [HttpGet]
    [RequirePermission(Permissions.AuditRead)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> Search(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? action,
        [FromQuery] string? entityName,
        [FromQuery] Guid? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
        => Success(await Mediator.Send(
            new GetAuditLogsQuery(from, to, action, entityName, userId, page, pageSize),
            cancellationToken));

    /// <summary>Distinct audit actions, for filter pickers.</summary>
    [HttpGet("actions")]
    [RequirePermission(Permissions.AuditRead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<string>>>> GetActions(CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetAuditActionsQuery(), cancellationToken));
}
