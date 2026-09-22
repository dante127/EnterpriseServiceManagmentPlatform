using Microsoft.AspNetCore.Authorization;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Authorization;

/// <summary>
/// Declarative permission check: <c>[RequirePermission(Permissions.TicketsAssign)]</c>.
/// Keeps magic policy strings out of controllers.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
    {
        Policy = Permissions.Policy(permission);
    }

    public string Permission => Policy is null ? string.Empty : Policy[Permissions.PolicyPrefix.Length..];
}
