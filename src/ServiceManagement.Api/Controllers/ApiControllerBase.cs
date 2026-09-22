using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Models;
using ServiceManagement.Infrastructure.Diagnostics;

namespace ServiceManagement.Api.Controllers;

/// <summary>
/// Shared plumbing for every controller: versioned route, the single response
/// envelope and the correlation id. Controllers contain no business logic -
/// they translate HTTP into a MediatR request and back.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ApiControllerBase(ISender mediator)
    {
        Mediator = mediator;
    }

    protected ISender Mediator { get; }

    protected string? CorrelationId
        => HttpContext.Items.TryGetValue(CorrelationIdDefaults.ItemsKey, out var value)
            ? value as string
            : null;

    protected ActionResult<ApiResponse<T>> Success<T>(T data, string? message = null)
        => Ok(ApiResponse<T>.Ok(data, message, CorrelationId));

    protected ActionResult<ApiResponse<T>> Created<T>(T data, string? message = null)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<T>.Ok(data, message, CorrelationId));

    /// <summary>Used by commands that have no payload to return.</summary>
    protected ActionResult<ApiResponse<object>> Acknowledge(string message = "Request completed.")
        => Ok(ApiResponse<object>.Ok(new { }, message, CorrelationId));
}
