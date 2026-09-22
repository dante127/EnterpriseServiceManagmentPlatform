namespace ServiceManagement.Domain.Sla;

/// <summary>Service level state of a ticket.</summary>
public enum SlaStatus
{
    WithinSla = 1,
    AtRisk = 2,
    Breached = 3,
    Completed = 4
}
