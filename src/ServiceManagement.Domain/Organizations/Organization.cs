using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Organizations;

/// <summary>Top-level tenant boundary.</summary>
public class Organization : AuditableEntity
{
    private readonly List<Department> _departments = [];
    private readonly List<Team> _teams = [];

    private Organization()
    {
    }

    public Organization(string name, string code, string? description = null)
    {
        Name = Guard.NotBlank(name, nameof(name), 200);
        Code = Guard.NotBlank(code, nameof(code), 32).ToUpperInvariant();
        Description = description?.Trim();
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<Department> Departments => _departments.AsReadOnly();

    public IReadOnlyCollection<Team> Teams => _teams.AsReadOnly();

    public Department AddDepartment(string name, string code, string? description = null)
    {
        var department = new Department(Id, name, code, description);
        _departments.Add(department);
        return department;
    }
}
