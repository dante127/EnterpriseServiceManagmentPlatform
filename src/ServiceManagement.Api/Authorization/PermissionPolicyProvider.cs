using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Api.Authorization;

/// <summary>
/// Builds authorization policies on demand from the policy name, so a new
/// permission never requires a registration line:
/// <c>permission:tickets.assign</c> becomes an authenticated policy requiring
/// that permission claim.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Permissions.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var permission = policyName[Permissions.PolicyPrefix.Length..];

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackProvider.GetPolicyAsync(policyName);
    }
}
