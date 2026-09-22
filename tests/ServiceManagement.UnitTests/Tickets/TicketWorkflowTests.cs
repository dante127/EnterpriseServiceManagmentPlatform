using FluentAssertions;
using ServiceManagement.Domain.Exceptions;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.UnitTests.TestData;

namespace ServiceManagement.UnitTests.Tickets;

public class TicketWorkflowTests
{
    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Assigned)]
    [InlineData(TicketStatus.New, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Assigned, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Pending)]
    [InlineData(TicketStatus.Pending, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress)]
    [InlineData(TicketStatus.New, TicketStatus.Cancelled)]
    public void Allowed_transitions_match_the_documented_workflow(TicketStatus from, TicketStatus to)
    {
        TicketWorkflow.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Resolved)]
    [InlineData(TicketStatus.New, TicketStatus.Closed)]
    [InlineData(TicketStatus.Assigned, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Cancelled, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Pending)]
    public void Forbidden_transitions_are_rejected(TicketStatus from, TicketStatus to)
    {
        TicketWorkflow.CanTransition(from, to).Should().BeFalse();

        var act = () => TicketWorkflow.EnsureTransition(from, to);

        act.Should()
            .Throw<InvalidStatusTransitionException>()
            .WithMessage($"*'{from}'*'{to}'*");
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Closed_and_cancelled_are_terminal(TicketStatus status)
    {
        TicketWorkflow.IsTerminal(status).Should().BeTrue();
        TicketWorkflow.AllowedFrom(status).Should().BeEmpty();
    }

    [Fact]
    public void A_ticket_cannot_skip_straight_from_new_to_resolved()
    {
        var ticket = TicketFactory.Create();

        var act = () => ticket.ChangeStatus(TicketStatus.Resolved, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidStatusTransitionException>();
        ticket.Status.Should().Be(TicketStatus.New);
    }

    [Fact]
    public void Repeating_the_current_status_is_rejected()
    {
        var ticket = TicketFactory.Create();

        var act = () => ticket.ChangeStatus(TicketStatus.New, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        act.Should().Throw<DomainRuleViolationException>();
    }
}
