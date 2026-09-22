using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Identity;

/// <summary>
/// A single atomic capability such as <c>tickets.assign</c>.
/// Permissions are the only thing authorization policies are built from.
/// </summary>
public class Permission : AuditableEntity
{
    private Permission()
    {
    }

    public Permission(string name, string group, string? description = null)
    {
        Name = Guard.NotBlank(name, nameof(name), 128).ToLowerInvariant();
        NormalizedName = Name.ToUpperInvariant();
        Group = Guard.NotBlank(group, nameof(group), 64);
        Description = description?.Trim();
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string Group { get; private set; } = string.Empty;

    public string? Description { get; private set; }
}
