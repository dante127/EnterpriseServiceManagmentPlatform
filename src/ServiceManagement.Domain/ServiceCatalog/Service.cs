using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Domain.ServiceCatalog;

/// <summary>
/// An offering users can raise tickets against (for example "IT Support").
/// The optional priority/SLA defaults are copied onto a ticket when it is created.
/// </summary>
public class Service : AuditableEntity
{
    private readonly List<Ticket> _tickets = [];

    private Service()
    {
    }

    public Service(
        Guid categoryId,
        string name,
        string code,
        string? description = null,
        Guid? defaultPriorityId = null,
        Guid? defaultSlaPolicyId = null)
    {
        CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));
        Name = Guard.NotBlank(name, nameof(name), 200);
        Code = Guard.NotBlank(code, nameof(code), 32).ToUpperInvariant();
        Description = description?.Trim();
        DefaultPriorityId = defaultPriorityId;
        DefaultSlaPolicyId = defaultSlaPolicyId;
        IsActive = true;
    }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? DefaultPriorityId { get; private set; }

    public Guid? DefaultSlaPolicyId { get; private set; }

    public bool IsActive { get; private set; }

    public ServiceCategory Category { get; private set; } = null!;

    public ServicePriority? DefaultPriority { get; private set; }

    public SlaPolicy? DefaultSlaPolicy { get; private set; }

    public IReadOnlyCollection<Ticket> Tickets => _tickets.AsReadOnly();

    public void UpdateDetails(string name, string? description)
    {
        Name = Guard.NotBlank(name, nameof(name), 200);
        Description = description?.Trim();
    }

    public void SetDefaults(Guid? defaultPriorityId, Guid? defaultSlaPolicyId)
    {
        DefaultPriorityId = defaultPriorityId;
        DefaultSlaPolicyId = defaultSlaPolicyId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
