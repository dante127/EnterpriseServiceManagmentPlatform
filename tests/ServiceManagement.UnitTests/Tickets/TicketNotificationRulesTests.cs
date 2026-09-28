using FluentAssertions;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.UnitTests.Tickets;

/// <summary>
/// Every ticket mutation ends with the same two steps: tell the affected people,
/// and record what changed. The cross-cutting noise in that tail is what these
/// tests cover, so the handlers themselves can stay about orchestration.
/// </summary>
public class TicketNotificationRulesTests
{
    private static Ticket ARequesterTicket(Guid requesterId, Guid? assigneeId = null, Guid? teamId = null)
    {
        var ticket = new Ticket(
            "TKT-000123",
            "Laptop will not boot",
            "Created by the notification-rule tests.",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            requesterId,
            DateTimeOffset.Parse("2026-03-01T09:00:00+00:00"));

        if (assigneeId is not null)
        {
            ticket.AssignToUser(assigneeId.Value, requesterId, "assigned", ticket.ReportedAt.AddMinutes(1));
        }

        if (teamId is not null)
        {
            ticket.AssignToTeam(teamId.Value, requesterId, "assigned to team", ticket.ReportedAt.AddMinutes(2));
        }

        return ticket;
    }

    [Fact]
    public void The_actor_is_never_notified_about_their_own_action()
    {
        var requester = Guid.CreateVersion7();
        var agent = Guid.CreateVersion7();
        var ticket = ARequesterTicket(requester, assigneeId: agent);

        var targets = TicketNotificationRules.ForStatusChange(ticket, actorId: requester);

        // The requester is the actor, so only the agent is left to tell.
        targets.Should().BeEquivalentTo([agent], "the requester must not be told about their own change");
    }

    [Fact]
    public void A_sole_participant_acting_on_their_own_ticket_notifies_nobody()
    {
        var requester = Guid.CreateVersion7();
        var ticket = ARequesterTicket(requester);

        TicketNotificationRules.ForStatusChange(ticket, actorId: requester)
            .Should().BeEmpty();
    }

    [Fact]
    public void Participants_are_de_duplicated_when_a_person_is_both_requester_and_assignee()
    {
        var requester = Guid.CreateVersion7();
        // A ticket that somehow ended up self-assigned: the person is both sides.
        var ticket = ARequesterTicket(requester, assigneeId: requester);

        var targets = TicketNotificationRules.ForStatusChange(ticket, actorId: Guid.CreateVersion7());

        targets.Should().ContainSingle("the requester must not be told twice");
    }

    [Fact]
    public void An_absent_assignee_contributes_nobody()
    {
        var requester = Guid.CreateVersion7();
        var ticket = ARequesterTicket(requester);

        var targets = TicketNotificationRules.ForStatusChange(ticket, actorId: Guid.CreateVersion7());

        // Previously this pattern used Guid.Empty as a sentinel, which is a real
        // Guid and would fail the Notifications foreign key.
        targets.Should().BeEquivalentTo([requester]);
    }

    [Fact]
    public void An_internal_comment_excludes_the_requester()
    {
        var requester = Guid.CreateVersion7();
        var agent = Guid.CreateVersion7();
        var ticket = ARequesterTicket(requester, assigneeId: agent);

        // Internal notes are for the support side: notifying the requester would
        // leak the note back to the person it is about.
        var targets = TicketNotificationRules.ForComment(ticket, actorId: agent, isInternal: true);

        targets.Should().BeEmpty("the requester is excluded and the agent is the actor");
    }

    [Fact]
    public void An_internal_comment_still_reaches_a_third_party_assignee()
    {
        var requester = Guid.CreateVersion7();
        var agent = Guid.CreateVersion7();
        var ticket = ARequesterTicket(requester, assigneeId: agent);

        var targets = TicketNotificationRules.ForComment(ticket, actorId: Guid.CreateVersion7(), isInternal: true);

        targets.Should().BeEquivalentTo([agent], "the requester is dropped, the agent is not");
    }

    [Fact]
    public void A_public_comment_reaches_both_the_requester_and_the_agent()
    {
        var requester = Guid.CreateVersion7();
        var agent = Guid.CreateVersion7();
        var ticket = ARequesterTicket(requester, assigneeId: agent);

        var targets = TicketNotificationRules.ForComment(ticket, actorId: Guid.CreateVersion7(), isInternal: false);

        targets.Should().BeEquivalentTo([requester, agent]);
    }

    [Fact]
    public void Ambient_caller_details_are_filled_in_from_the_current_user()
    {
        var user = new StubCurrentUser { UserId = Guid.CreateVersion7(), IpAddress = "10.0.0.7", CorrelationId = "abc-123" };

        var entry = TicketAuditEntry.For("Ticket.Updated", "Ticket", "42", user);

        entry.UserId.Should().Be(user.UserId);
        entry.IpAddress.Should().Be("10.0.0.7");
        entry.CorrelationId.Should().Be("abc-123");
    }

    [Fact]
    public void An_explicit_value_wins_over_the_ambient_one()
    {
        var user = new StubCurrentUser { UserId = Guid.CreateVersion7() };
        var actingUser = Guid.CreateVersion7();

        // The assignment handlers pass the resolved user id explicitly, because
        // RequireUserId is what proved the request was authenticated.
        var entry = TicketAuditEntry.For("Ticket.Assigned", "Ticket", "42", user, userId: actingUser);

        entry.UserId.Should().Be(actingUser);
    }

    [Fact]
    public void An_anonymous_actor_leaves_the_user_id_unset_rather_than_inventing_one()
    {
        var user = new StubCurrentUser { UserId = null };

        var entry = TicketAuditEntry.For("Ticket.Updated", "Ticket", "42", user);

        entry.UserId.Should().BeNull();
    }

    private sealed class StubCurrentUser : ICurrentUserService
    {
        public Guid? UserId { get; init; }

        public string? UserName => "stub";

        public string? Email => "stub@test.local";

        public bool IsAuthenticated => UserId is not null;

        public IReadOnlyCollection<string> Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public string? IpAddress { get; init; }

        public string? CorrelationId { get; init; }

        public bool HasPermission(string permission) => true;

        public Guid RequireUserId() => UserId ?? Guid.CreateVersion7();
    }
}
