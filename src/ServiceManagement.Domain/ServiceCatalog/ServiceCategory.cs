using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.ServiceCatalog;

/// <summary>Hierarchical classification of services (for example: Hardware &gt; Laptop).</summary>
public class ServiceCategory : AuditableEntity
{
    private readonly List<ServiceCategory> _children = [];
    private readonly List<Service> _services = [];

    private ServiceCategory()
    {
    }

    public ServiceCategory(string name, string code, string? description = null, Guid? parentId = null)
    {
        Name = Guard.NotBlank(name, nameof(name), 200);
        Code = Guard.NotBlank(code, nameof(code), 32).ToUpperInvariant();
        Description = description?.Trim();
        ParentId = parentId;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? ParentId { get; private set; }

    public bool IsActive { get; private set; }

    public ServiceCategory? Parent { get; private set; }

    public IReadOnlyCollection<ServiceCategory> Children => _children.AsReadOnly();

    public IReadOnlyCollection<Service> Services => _services.AsReadOnly();

    public void UpdateDetails(string name, string? description)
    {
        Name = Guard.NotBlank(name, nameof(name), 200);
        Description = description?.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
