using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.ServiceCatalog;

/// <summary>
/// Service level agreement for a priority. Deadlines are never hardcoded in
/// code: the SLA engine reads the active policy from the database.
/// </summary>
public class SlaPolicy : AuditableEntity
{
    private SlaPolicy()
    {
    }

    public SlaPolicy(
        string name,
        Guid priorityId,
        int firstResponseMinutes,
        int resolutionMinutes,
        int atRiskThresholdPercent = 20)
    {
        Name = Guard.NotBlank(name, nameof(name), 200);
        PriorityId = Guard.NotEmpty(priorityId, nameof(priorityId));
        FirstResponseMinutes = Guard.Positive(firstResponseMinutes, nameof(firstResponseMinutes));
        ResolutionMinutes = Guard.Positive(resolutionMinutes, nameof(resolutionMinutes));
        AtRiskThresholdPercent = Guard.InRange(atRiskThresholdPercent, 1, 99, nameof(atRiskThresholdPercent));
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public Guid PriorityId { get; private set; }

    /// <summary>Minutes allowed before the first agent response is due.</summary>
    public int FirstResponseMinutes { get; private set; }

    /// <summary>Minutes allowed before the ticket must be resolved.</summary>
    public int ResolutionMinutes { get; private set; }

    /// <summary>Percentage of the window after which the ticket becomes AtRisk.</summary>
    public int AtRiskThresholdPercent { get; private set; }

    public bool IsActive { get; private set; }

    public ServicePriority Priority { get; private set; } = null!;

    public void UpdateTargets(int firstResponseMinutes, int resolutionMinutes, int atRiskThresholdPercent)
    {
        FirstResponseMinutes = Guard.Positive(firstResponseMinutes, nameof(firstResponseMinutes));
        ResolutionMinutes = Guard.Positive(resolutionMinutes, nameof(resolutionMinutes));
        AtRiskThresholdPercent = Guard.InRange(atRiskThresholdPercent, 1, 99, nameof(atRiskThresholdPercent));
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
