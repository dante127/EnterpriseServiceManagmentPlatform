using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Identity;

/// <summary>A named group of permissions.</summary>
public class Role : AuditableEntity
{
    private readonly List<RolePermission> _rolePermissions = [];
    private readonly List<UserRole> _userRoles = [];

    private Role()
    {
    }

    public Role(string name, string? description = null, bool isSystemRole = false)
    {
        Name = Guard.NotBlank(name, nameof(name), 128);
        NormalizedName = Name.ToUpperInvariant();
        Description = description?.Trim();
        IsSystemRole = isSystemRole;
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsSystemRole { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    public void Rename(string name)
    {
        Name = Guard.NotBlank(name, nameof(name), 128);
        NormalizedName = Name.ToUpperInvariant();
    }

    public void GrantPermission(Guid permissionId)
    {
        if (_rolePermissions.Any(rp => rp.PermissionId == permissionId))
        {
            return;
        }

        _rolePermissions.Add(new RolePermission(Id, permissionId));
    }

    public void RevokePermission(Guid permissionId)
        => _rolePermissions.RemoveAll(rp => rp.PermissionId == permissionId);
}
