using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Application.Common.Security;

/// <summary>
/// Loads the role and permission set of a user. Kept in one place so both the
/// login flow and the token refresh flow agree on what a principal may do.
/// </summary>
public static class UserSecurityQueryService
{
    public static async Task<SecurityProfile> LoadAsync(
        IApplicationDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var roles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        var permissions = await dbContext.RolePermissions
            .AsNoTracking()
            .Where(rolePermission => dbContext.UserRoles
                .Any(userRole => userRole.UserId == userId && userRole.RoleId == rolePermission.RoleId))
            .Select(rolePermission => rolePermission.Permission.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new SecurityProfile(roles, permissions);
    }

    public static Task<SecurityProfile> LoadAsync(
        IApplicationDbContext dbContext,
        Guid userId)
        => LoadAsync(dbContext, userId, CancellationToken.None);
}

/// <summary>Roles and permissions granted to a principal.</summary>
public sealed record SecurityProfile(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
