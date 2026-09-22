using ServiceManagement.Domain.Exceptions;

namespace ServiceManagement.Domain.Tickets;

/// <summary>
/// The single source of truth for allowed ticket status transitions.
/// Arbitrary status writes are impossible: every change goes through
/// <see cref="EnsureTransition"/>.
/// </summary>
public static class TicketWorkflow
{
    private static readonly Dictionary<TicketStatus, TicketStatus[]> AllowedTransitions = new()
    {
        [TicketStatus.New] = [TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.Cancelled],
        [TicketStatus.Assigned] = [TicketStatus.InProgress, TicketStatus.Pending, TicketStatus.Cancelled],
        [TicketStatus.InProgress] = [TicketStatus.Pending, TicketStatus.Resolved, TicketStatus.Cancelled],
        [TicketStatus.Pending] = [TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Cancelled],
        [TicketStatus.Resolved] = [TicketStatus.Closed, TicketStatus.InProgress],
        [TicketStatus.Closed] = [],
        [TicketStatus.Cancelled] = []
    };

    public static IReadOnlyCollection<TicketStatus> AllowedFrom(TicketStatus status)
        => AllowedTransitions.TryGetValue(status, out var allowed) ? allowed : [];

    public static bool CanTransition(TicketStatus from, TicketStatus to)
        => AllowedFrom(from).Contains(to);

    public static bool IsTerminal(TicketStatus status)
        => status is TicketStatus.Closed or TicketStatus.Cancelled;

    public static void EnsureTransition(TicketStatus from, TicketStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidStatusTransitionException(from.ToString(), to.ToString());
        }
    }
}
