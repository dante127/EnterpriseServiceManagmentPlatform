namespace ServiceManagement.Domain.Tickets;

/// <summary>Lifecycle states of a ticket. Transitions are enforced by <see cref="TicketWorkflow"/>.</summary>
public enum TicketStatus
{
    New = 1,
    Assigned = 2,
    InProgress = 3,
    Pending = 4,
    Resolved = 5,
    Closed = 6,
    Cancelled = 7
}
