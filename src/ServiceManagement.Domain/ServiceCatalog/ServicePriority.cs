using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.ServiceCatalog;

/// <summary>
/// Ticket priority. <see cref="Level"/> drives ordering (1 = most urgent) and
/// is what the SLA policy lookup keys on.
/// </summary>
public class ServicePriority : AuditableEntity
{
    private readonly List<SlaPolicy> _slaPolicies = [];

    private ServicePriority()
    {
    }

    public ServicePriority(string name, string code, int level, string? colorHex = null)
    {
        Name = Guard.NotBlank(name, nameof(name), 100);
        Code = Guard.NotBlank(code, nameof(code), 32).ToUpperInvariant();
        Level = Guard.InRange(level, 1, 99, nameof(level));
        ColorHex = colorHex?.Trim();
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public string? ColorHex { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<SlaPolicy> SlaPolicies => _slaPolicies.AsReadOnly();
}
