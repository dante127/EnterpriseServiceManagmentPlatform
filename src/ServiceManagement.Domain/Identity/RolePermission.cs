using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Identity;

/// <summary>Explicit join entity between <see cref="Role"/> and <see cref="Permission"/>.</summary>
public class RolePermission : BaseEntity
{
    private RolePermission()
    {
    }

    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }

    public Role Role { get; private set; } = null!;

    public Permission Permission { get; private set; } = null!;
}
