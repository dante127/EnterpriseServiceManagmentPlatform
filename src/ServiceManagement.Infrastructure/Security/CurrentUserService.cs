using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Infrastructure.Diagnostics;

namespace ServiceManagement.Infrastructure.Security;

/// <summary>Reads the caller identity from the validated JWT on the current request.</summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;

            var value = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal?.FindFirstValue("sub");

            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public string? UserName => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);

    public string? Email => _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles
        => _httpContextAccessor.HttpContext?.User
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToList() ?? [];

    public IReadOnlyCollection<string> Permissions
        => _httpContextAccessor.HttpContext?.User
            .FindAll(PermissionClaims.Permission)
            .Select(claim => claim.Value)
            .ToList() ?? [];

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext is null)
            {
                return null;
            }

            if (httpContext.Items.TryGetValue(CorrelationIdDefaults.ItemsKey, out var value) &&
                value is string correlationId)
            {
                return correlationId;
            }

            return httpContext.Request.Headers[CorrelationIdDefaults.HeaderName].FirstOrDefault();
        }
    }

    public bool HasPermission(string permission)
        => PermissionEvaluator.HasPermission(Permissions, permission);

    public Guid RequireUserId()
        => UserId ?? throw new UnauthorizedException("Authentication is required for this operation.");
}
