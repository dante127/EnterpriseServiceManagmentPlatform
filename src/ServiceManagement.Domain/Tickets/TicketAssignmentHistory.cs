using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Tickets;

/// <summary>
/// Append-only audit trail of every assignment or reassignment decision.
/// </summary>
public class TicketAssignmentHistory : BaseEntity
{
    private TicketAssignmentHistory()
    {
    }

    public TicketAssignmentHistory(
        Guid ticketId,
        Guid? previousAssigneeId,
        Guid? newAssigneeId,
        Guid? previousTeamId,
        Guid? newTeamId,
        string? reason,
        Guid changedBy,
        DateTimeOffset changedAt)
    {
        TicketId = Guard.NotEmpty(ticketId, nameof(ticketId));
        PreviousAssigneeId = previousAssigneeId;
        NewAssigneeId = newAssigneeId;
        PreviousTeamId = previousTeamId;
        NewTeamId = newTeamId;
        Reason = reason?.Trim();
        ChangedBy = Guard.NotEmpty(changedBy, nameof(changedBy));
        ChangedAt = changedAt;
    }

    public Guid TicketId { get; private set; }

    public Guid? PreviousAssigneeId { get; private set; }

    public Guid? NewAssigneeId { get; private set; }

    public Guid? PreviousTeamId { get; private set; }

    public Guid? NewTeamId { get; private set; }

    public string? Reason { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public Ticket Ticket { get; private set; } = null!;
}
