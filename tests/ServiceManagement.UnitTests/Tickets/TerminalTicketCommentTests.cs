using FluentAssertions;
using ServiceManagement.Domain.Exceptions;
using ServiceManagement.Domain.Tickets;
using Xunit;

namespace ServiceManagement.UnitTests.Tickets;

/// <summary>
/// Comments on a finished ticket are allowed on purpose. Agents routinely
/// annotate a closed ticket after the fact - a correction, a follow-up finding,
/// a note for the audit trail - and blocking that would lose information the
/// business wants to keep.
/// <para>
/// This is a stated decision rather than an oversight. Every other mutation
/// (details, priority, assignment, status) calls <c>EnsureNotTerminal</c>;
/// <c>AddComment</c> deliberately does not, and the behaviour is pinned here so a
/// future contributor does not "fix" it into a bug.
/// </para>
/// </summary>
public class TerminalTicketCommentTests
{
    private static Ticket ATicketIn(TicketStatus status)
    {
        var requester = Guid.CreateVersion7();
        var agent = Guid.CreateVersion7();
        var reportedAt = DateTimeOffset.Parse("2026-03-01T09:00:00+00:00");

        var ticket = new Ticket(
            "TKT-000777",
            "Laptop will not boot",
            "Terminal-status comment tests.",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            requester,
            reportedAt);

        // Walk the workflow to the requested state rather than forcing the
        // property, so the ticket is genuinely in a terminal state.
        if (status is TicketStatus.InProgress or TicketStatus.Resolved or TicketStatus.Closed)
        {
            ticket.ChangeStatus(TicketStatus.InProgress, agent, reportedAt.AddMinutes(1));
        }

        if (status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            ticket.ChangeStatus(TicketStatus.Resolved, agent, reportedAt.AddMinutes(2));
        }

        if (status is TicketStatus.Closed)
        {
            ticket.ChangeStatus(TicketStatus.Closed, requester, reportedAt.AddMinutes(3));
        }

        if (status is TicketStatus.Cancelled)
        {
            ticket.ChangeStatus(TicketStatus.Cancelled, requester, reportedAt.AddMinutes(1));
        }

        return ticket;
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    [InlineData(TicketStatus.Resolved)]
    public void A_comment_is_accepted_on_a_finished_ticket(TicketStatus status)
    {
        var ticket = ATicketIn(status);
        var at = ticket.ReportedAt.AddHours(1);

        var act = () => ticket.AddComment("Post-closure note.", Guid.CreateVersion7(), isInternal: false, at);

        act.Should().NotThrow("annotating a closed ticket is a deliberate feature");
    }

    [Fact]
    public void The_comment_is_retained_with_its_audit_trail()
    {
        var ticket = ATicketIn(TicketStatus.Closed);
        var authorId = Guid.CreateVersion7();
        var at = ticket.ReportedAt.AddHours(1);

        var comment = ticket.AddComment("Post-closure note.", authorId, isInternal: true, at);

        comment.TicketId.Should().Be(ticket.Id);
        comment.AuthorId.Should().Be(authorId);
        comment.IsInternal.Should().BeTrue();
        comment.CreatedAt.Should().Be(at);
    }

    [Fact]
    public void Other_mutations_are_still_blocked_on_a_finished_ticket()
    {
        // The contrast that makes this a decision rather than a missing guard:
        // commenting is exempt, changing the ticket is not.
        var ticket = ATicketIn(TicketStatus.Closed);

        var reassign = () => ticket.AssignToUser(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "reassign after close",
            ticket.ReportedAt.AddHours(1));

        var edit = () => ticket.UpdateDetails(
            "New title",
            "New description",
            Guid.CreateVersion7(),
            ticket.PriorityId);

        reassign.Should().Throw<DomainRuleViolationException>();
        edit.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void A_cancelled_ticket_cannot_be_reopened_by_commenting()
    {
        // Allowing a comment must not become a back door into the workflow.
        var ticket = ATicketIn(TicketStatus.Cancelled);

        ticket.AddComment("A note.", Guid.CreateVersion7(), isInternal: false, ticket.ReportedAt.AddHours(1));

        ticket.Status.Should().Be(TicketStatus.Cancelled, "a comment is not a status transition");
    }
}
