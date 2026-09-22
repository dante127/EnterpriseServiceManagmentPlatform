using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Reports;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Controllers;

/// <summary>Operational reporting.</summary>
[ApiVersion("1.0")]
public sealed class ReportsController : ApiControllerBase
{
    public ReportsController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>
    /// Dashboard aggregates: volumes, status/priority/SLA breakdown and the most
    /// recent tickets. Cached for a short window.
    /// </summary>
    [HttpGet("dashboard")]
    [RequirePermission(Permissions.ReportsRead)]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> GetDashboard(CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetDashboardQuery(), cancellationToken));
}
