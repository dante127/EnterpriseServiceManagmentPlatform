using Microsoft.AspNetCore.Authorization;
using ServiceManagement.Application.Common.Security;

namespace ServiceManagement.Api.Authorization;

/// <summary>Authorization requirement carrying the permission a policy demands.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    public string Permission { get; }
}

/// <summary>
/// Satisfies a requirement when the validated JWT contains the permission claim.
/// Authorization never inspects role names.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var granted = context.User
            .FindAll(PermissionClaims.Permission)
            .Select(claim => claim.Value);

        if (PermissionEvaluator.HasPermission(granted, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
