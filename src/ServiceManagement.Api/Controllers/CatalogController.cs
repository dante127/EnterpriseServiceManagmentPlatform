using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ServiceManagement.Api.Authorization;
using ServiceManagement.Api.Models;
using ServiceManagement.Application.Catalog;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Controllers;

/// <summary>Service catalog: categories, priorities, SLA policies and services.</summary>
[ApiVersion("1.0")]
public sealed class CatalogController : ApiControllerBase
{
    public CatalogController(ISender mediator)
        : base(mediator)
    {
    }

    /// <summary>Lists services (cached).</summary>
    [HttpGet("services")]
    [RequirePermission(Permissions.CatalogRead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServiceDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ServiceDto>>>> GetServices(
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetServicesQuery(), cancellationToken));

    /// <summary>Lists service categories (cached).</summary>
    [HttpGet("categories")]
    [RequirePermission(Permissions.CatalogRead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServiceCategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ServiceCategoryDto>>>> GetCategories(
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetServiceCategoriesQuery(), cancellationToken));

    /// <summary>Lists priorities (cached).</summary>
    [HttpGet("priorities")]
    [RequirePermission(Permissions.CatalogRead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServicePriorityDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ServicePriorityDto>>>> GetPriorities(
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetServicePrioritiesQuery(), cancellationToken));

    /// <summary>Lists SLA policies (cached).</summary>
    [HttpGet("sla-policies")]
    [RequirePermission(Permissions.CatalogRead)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SlaPolicyDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SlaPolicyDto>>>> GetSlaPolicies(
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(new GetSlaPoliciesQuery(), cancellationToken));

    /// <summary>Creates a service category.</summary>
    [HttpPost("categories")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(ApiResponse<ServiceCategoryDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ServiceCategoryDto>>> CreateCategory(
        [FromBody] CreateServiceCategoryCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Category created.");

    /// <summary>Creates a priority.</summary>
    [HttpPost("priorities")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(ApiResponse<ServicePriorityDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ServicePriorityDto>>> CreatePriority(
        [FromBody] CreateServicePriorityCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Priority created.");

    /// <summary>Creates an SLA policy for a priority.</summary>
    [HttpPost("sla-policies")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(ApiResponse<SlaPolicyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SlaPolicyDto>>> CreateSlaPolicy(
        [FromBody] CreateSlaPolicyCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "SLA policy created.");

    /// <summary>Updates the targets of an SLA policy.</summary>
    [HttpPut("sla-policies/{id:guid}")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(ApiResponse<SlaPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SlaPolicyDto>>> UpdateSlaPolicy(
        Guid id,
        [FromBody] UpdateSlaPolicyRequest request,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(
            new UpdateSlaPolicyCommand(id, request.FirstResponseMinutes, request.ResolutionMinutes, request.AtRiskThresholdPercent),
            cancellationToken));

    /// <summary>Creates a service.</summary>
    [HttpPost("services")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(ApiResponse<ServiceDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ServiceDto>>> CreateService(
        [FromBody] CreateServiceCommand command,
        CancellationToken cancellationToken)
        => Created(await Mediator.Send(command, cancellationToken), "Service created.");

    /// <summary>Updates a service.</summary>
    [HttpPut("services/{id:guid}")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(ApiResponse<ServiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ServiceDto>>> UpdateService(
        Guid id,
        [FromBody] UpdateServiceRequest request,
        CancellationToken cancellationToken)
        => Success(await Mediator.Send(
            new UpdateServiceCommand(id, request.Name, request.Description, request.DefaultPriorityId, request.DefaultSlaPolicyId),
            cancellationToken));
}

/// <summary>SLA policy update payload.</summary>
public sealed record UpdateSlaPolicyRequest(int FirstResponseMinutes, int ResolutionMinutes, int AtRiskThresholdPercent);

/// <summary>Service update payload.</summary>
public sealed record UpdateServiceRequest(string Name, string? Description, Guid? DefaultPriorityId, Guid? DefaultSlaPolicyId);

