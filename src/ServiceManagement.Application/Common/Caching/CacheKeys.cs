namespace ServiceManagement.Application.Common.Caching;

/// <summary>
/// Central cache key catalogue. Keys live in one place so invalidation stays
/// auditable and typo-proof.
/// </summary>
public static class CacheKeys
{
    public const string Services = "catalog:services:all";
    public const string ServiceCategories = "catalog:categories:all";
    public const string ServicePriorities = "catalog:priorities:all";
    public const string SlaPolicies = "catalog:sla-policies:all";
    public const string Permissions = "identity:permissions:all";
    public const string Dashboard = "reports:dashboard";

    public static string UserPermissions(Guid userId) => $"identity:user-permissions:{userId}";

    public static string UserRoles(Guid userId) => $"identity:user-roles:{userId}";
}
