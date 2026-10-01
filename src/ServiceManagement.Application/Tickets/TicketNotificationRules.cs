using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Who to tell about a ticket change.
/// <para>
/// Every ticket mutation ends by notifying the people involved. The rules are
/// the same each time and easy to get subtly wrong inline: an actor must not be
/// notified about their own action, a person who is both requester and assignee
/// must not be told twice, and an absent assignee must contribute nobody rather
/// than an empty id that later fails a foreign key.
/// </para>
/// </summary>
internal static class TicketNotificationRules
{
    /// <summary>
    /// Resolves the recipients of a ticket notification: the requester and the
    /// assignee, minus the actor, without duplicates.
    /// </summary>
    /// <param name="actorId">The user who made the change.</param>
    /// <param name="isInternal">
    /// True for internal notes, which must not reach the requester. The requester
    /// is passed separately from the assignee so this can genuinely drop them;
    /// folding both into one candidate list would make the flag a no-op.
    /// </param>
    public static IReadOnlyList<Guid> ForComment(
        Ticket ticket,
        Guid actorId,
        bool isInternal)
        => Build(
            new[] { isInternal ? null : ticket.RequesterId, ticket.AssignedToId },
            actorId);

    /// <summary>
    /// Resolves the recipients of a status change: the requester and the assignee.
    /// </summary>
    public static IReadOnlyList<Guid> ForStatusChange(Ticket ticket, Guid actorId)
        => Build(new[] { ticket.RequesterId, ticket.AssignedToId }, actorId);

    /// <summary>
    /// Resolves the recipients when a ticket is created already assigned to a
    /// team: every member of the team, minus the actor who raised it.
    /// </summary>
    public static IReadOnlyList<Guid> ForTeamAssignment(IEnumerable<Guid> memberIds, Guid actorId)
        => Build(memberIds.Select(memberId => (Guid?)memberId), actorId);

    private static IReadOnlyList<Guid> Build(IEnumerable<Guid?> candidates, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => candidate is not null && candidate.Value != Guid.Empty)
            .Select(candidate => candidate!.Value)
            .Where(candidate => candidate != actorId)
            .Distinct()
            .ToList();
    }
}
