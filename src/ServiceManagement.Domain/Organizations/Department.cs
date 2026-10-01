using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Organizations;

/// <summary>A functional area inside an organization.</summary>
public class Department : AuditableEntity
{
    private readonly List<Team> _teams = [];

    private Department()
    {
    }

    public Department(Guid organizationId, string name, string code, string? description = null)
    {
        OrganizationId = Guard.NotEmpty(organizationId, nameof(organizationId));
        Name = Guard.NotBlank(name, nameof(name), 200);
        Code = Guard.NotBlank(code, nameof(code), 32).ToUpperInvariant();
        Description = description?.Trim();
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Organization Organization { get; private set; } = null!;

    public IReadOnlyCollection<Team> Teams => _teams.AsReadOnly();
}
