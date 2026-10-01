using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Exceptions;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Domain.Identity;

/// <summary>
/// Application user. Passwords are never stored: only an
/// ASP.NET Core Identity PBKDF2 hash produced by the infrastructure layer.
/// </summary>
public class User : AuditableEntity
{
    private readonly List<UserRole> _userRoles = [];
    private readonly List<RefreshToken> _refreshTokens = [];
    private readonly List<TeamMember> _teamMemberships = [];

    private User()
    {
        // Required by EF Core materialization.
    }

    public User(
        string email,
        string userName,
        string firstName,
        string lastName,
        string passwordHash)
    {
        Email = Guard.NotBlank(email, nameof(email), 256).ToLowerInvariant();
        UserName = Guard.NotBlank(userName, nameof(userName), 128);
        FirstName = Guard.NotBlank(firstName, nameof(firstName), 128);
        LastName = Guard.NotBlank(lastName, nameof(lastName), 128);
        PasswordHash = Guard.NotBlank(passwordHash, nameof(passwordHash), 512);
        SecurityStamp = Guid.NewGuid().ToString("N");
        IsActive = true;
    }

    public string Email { get; private set; } = string.Empty;

    public string UserName { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public string SecurityStamp { get; private set; } = string.Empty;

    public DateTimeOffset? LastLoginAt { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles.AsReadOnly();

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    public IReadOnlyCollection<TeamMember> TeamMemberships => _teamMemberships.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}".Trim();

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = Guard.NotBlank(passwordHash, nameof(passwordHash), 512);

        // Rotating the security stamp invalidates every previously issued access token.
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        IsActive = false;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public void RecordLogin(DateTimeOffset at) => LastLoginAt = at;

    public void AssignToOrganization(Guid? organizationId, Guid? departmentId)
    {
        OrganizationId = organizationId;
        DepartmentId = departmentId;
    }

    public void AddRole(Guid roleId)
    {
        if (_userRoles.Any(ur => ur.RoleId == roleId))
        {
            return;
        }

        _userRoles.Add(new UserRole(Id, roleId));
    }

    public void RemoveRole(Guid roleId)
        => _userRoles.RemoveAll(ur => ur.RoleId == roleId);

    public void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainRuleViolationException("The account is deactivated and cannot be used.");
        }
    }
}
