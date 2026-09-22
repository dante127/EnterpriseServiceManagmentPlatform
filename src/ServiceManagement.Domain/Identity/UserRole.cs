using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Identity;

/// <summary>Explicit join entity between <see cref="User"/> and <see cref="Role"/>.</summary>
public class UserRole : BaseEntity
{
    private UserRole()
    {
    }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public User User { get; private set; } = null!;

    public Role Role { get; private set; } = null!;
}
