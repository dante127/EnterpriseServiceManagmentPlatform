using FluentAssertions;
using ServiceManagement.Domain.Exceptions;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.UnitTests.TestData;

namespace ServiceManagement.UnitTests.Tickets;

public class TicketAggregateTests
{
    [Fact]
    public void A_new_ticket_starts_as_new_and_has_no_assignee()
    {
        var ticket = TicketFactory.Create();

        ticket.Status.Should().Be(TicketStatus.New);
        ticket.AssignedToId.Should().BeNull();
        ticket.AssignedTeamId.Should().BeNull();
        ticket.SlaStatus.Should().Be(SlaStatus.WithinSla);
    }

    [Fact]
    public void Assigning_a_new_ticket_moves_it_to_assigned_and_records_history()
    {
        var ticket = TicketFactory.Create();
        var assignee = Guid.CreateVersion7();
        var changedBy = Guid.CreateVersion7();

        ticket.AssignToUser(assignee, changedBy, "First line owns hardware incidents.", TicketFactory.DefaultReportedAt.AddMinutes(5));

        ticket.Status.Should().Be(TicketStatus.Assigned);
        ticket.AssignedToId.Should().Be(assignee);
        ticket.AssignmentHistory.Should().HaveCount(1);

        var entry = ticket.AssignmentHistory.Single();
        entry.PreviousAssigneeId.Should().BeNull();
        entry.NewAssigneeId.Should().Be(assignee);
        entry.Reason.Should().Be("First line owns hardware incidents.");
        entry.ChangedBy.Should().Be(changedBy);
    }

    [Fact]
    public void Reassigning_keeps_the_previous_assignee_in_the_trail()
    {
        var ticket = TicketFactory.Create();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        ticket.AssignToUser(first, first, "initial", TicketFactory.DefaultReportedAt.AddMinutes(1));
        ticket.AssignToUser(second, first, "escalated", TicketFactory.DefaultReportedAt.AddMinutes(30));

        ticket.AssignedToId.Should().Be(second);
        ticket.AssignmentHistory.Should().HaveCount(2);

        var latest = ticket.AssignmentHistory.Last();
        latest.PreviousAssigneeId.Should().Be(first);
        latest.NewAssigneeId.Should().Be(second);
    }

    [Fact]
    public void Assigning_a_team_keeps_the_queue_and_the_trail()
    {
        var ticket = TicketFactory.Create();
        var team = Guid.CreateVersion7();

        ticket.AssignToTeam(team, Guid.CreateVersion7(), "Queued for the service desk.", TicketFactory.DefaultReportedAt.AddMinutes(2));

        ticket.AssignedTeamId.Should().Be(team);
        ticket.Status.Should().Be(TicketStatus.Assigned);
        ticket.AssignmentHistory.Should().ContainSingle(entry => entry.NewTeamId == team);
    }

    [Fact]
    public void Applying_an_sla_policy_sets_both_deadlines_from_the_report_time()
    {
        var ticket = TicketFactory.CreateWithSla(firstResponseMinutes: 30, resolutionMinutes: 240);

        ticket.FirstResponseDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(30));
        ticket.ResolutionDueAt.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(240));
    }

    [Fact]
    public void Changing_the_priority_clears_the_stale_deadlines_until_a_policy_is_applied()
    {
        var ticket = TicketFactory.CreateWithSla();

        ticket.ChangePriority(Guid.CreateVersion7());

        ticket.FirstResponseDueAt.Should().BeNull();
        ticket.ResolutionDueAt.Should().BeNull();
    }

    [Fact]
    public void Updating_details_with_the_same_priority_keeps_the_sla_deadlines()
    {
        // Regression guard: UpdateDetails used to call ChangePriority unconditionally,
        // which nulled both deadlines on every edit and left the ticket permanently
        // invisible to the SLA monitor.
        var ticket = TicketFactory.CreateWithSla(firstResponseMinutes: 30, resolutionMinutes: 240);
        var expectedFirstResponse = ticket.FirstResponseDueAt;
        var expectedResolution = ticket.ResolutionDueAt;

        ticket.UpdateDetails(
            "Laptop will not boot after update",
            "Still a black screen.",
            Guid.CreateVersion7(),
            ticket.PriorityId);

        ticket.FirstResponseDueAt.Should().Be(expectedFirstResponse);
        ticket.ResolutionDueAt.Should().Be(expectedResolution);
    }

    [Fact]
    public void Updating_details_with_a_different_priority_clears_the_stale_deadlines()
    {
        var ticket = TicketFactory.CreateWithSla();
        var newPriority = Guid.CreateVersion7();

        ticket.UpdateDetails("Escalated", "Escalating this incident.", Guid.CreateVersion7(), newPriority);

        ticket.PriorityId.Should().Be(newPriority);
        ticket.FirstResponseDueAt.Should().BeNull();
        ticket.ResolutionDueAt.Should().BeNull();
    }

    [Fact]
    public void Refresh_sla_status_promotes_a_late_first_response_to_breached()
    {
        var ticket = TicketFactory.CreateWithSla(firstResponseMinutes: 30, resolutionMinutes: 240);

        ticket.RefreshSlaStatus(TicketFactory.DefaultReportedAt.AddMinutes(45), atRiskThresholdPercent: 20);

        ticket.SlaStatus.Should().Be(SlaStatus.Breached);
    }

    [Fact]
    public void Refresh_sla_status_marks_a_closed_ticket_as_completed()
    {
        var ticket = TicketFactory.CreateWithSla();
        var actor = Guid.CreateVersion7();
        var now = TicketFactory.DefaultReportedAt.AddMinutes(10);

        ticket.AssignToUser(actor, actor, null, now);
        ticket.ChangeStatus(TicketStatus.InProgress, actor, now.AddMinutes(1));
        ticket.ChangeStatus(TicketStatus.Resolved, actor, now.AddMinutes(30));

        ticket.RefreshSlaStatus(TicketFactory.DefaultReportedAt.AddHours(6), atRiskThresholdPercent: 20);

        ticket.SlaStatus.Should().Be(SlaStatus.Completed);
        ticket.ResolvedAt.Should().Be(now.AddMinutes(30));
    }

    [Fact]
    public void Moving_to_in_progress_stamps_the_first_response_once()
    {
        var ticket = TicketFactory.CreateWithSla();
        var actor = Guid.CreateVersion7();

        ticket.AssignToUser(actor, actor, null, TicketFactory.DefaultReportedAt.AddMinutes(2));
        ticket.ChangeStatus(TicketStatus.InProgress, actor, TicketFactory.DefaultReportedAt.AddMinutes(10));

        var firstResponse = ticket.FirstRespondedAt;
        firstResponse.Should().Be(TicketFactory.DefaultReportedAt.AddMinutes(10));

        ticket.ChangeStatus(TicketStatus.Pending, actor, TicketFactory.DefaultReportedAt.AddMinutes(20));
        ticket.ChangeStatus(TicketStatus.InProgress, actor, TicketFactory.DefaultReportedAt.AddMinutes(30));

        // The stamp records the first response, not the latest one.
        ticket.FirstRespondedAt.Should().Be(firstResponse);
    }

    [Fact]
    public void Reopening_a_resolved_ticket_clears_the_resolution_timestamp()
    {
        var ticket = TicketFactory.CreateWithSla();
        var actor = Guid.CreateVersion7();

        ticket.AssignToUser(actor, actor, null, TicketFactory.DefaultReportedAt.AddMinutes(2));
        ticket.ChangeStatus(TicketStatus.InProgress, actor, TicketFactory.DefaultReportedAt.AddMinutes(5));
        ticket.ChangeStatus(TicketStatus.Resolved, actor, TicketFactory.DefaultReportedAt.AddMinutes(40));
        ticket.ChangeStatus(TicketStatus.InProgress, actor, TicketFactory.DefaultReportedAt.AddMinutes(60));

        ticket.ResolvedAt.Should().BeNull();
        ticket.ClosedAt.Should().BeNull();
    }

    [Fact]
    public void A_closed_ticket_can_no_longer_be_modified()
    {
        var ticket = TicketFactory.CreateWithSla();
        var actor = Guid.CreateVersion7();

        ticket.AssignToUser(actor, actor, null, TicketFactory.DefaultReportedAt.AddMinutes(1));
        ticket.ChangeStatus(TicketStatus.InProgress, actor, TicketFactory.DefaultReportedAt.AddMinutes(2));
        ticket.ChangeStatus(TicketStatus.Resolved, actor, TicketFactory.DefaultReportedAt.AddMinutes(3));
        ticket.ChangeStatus(TicketStatus.Closed, actor, TicketFactory.DefaultReportedAt.AddMinutes(4));

        var act = () => ticket.AssignToUser(Guid.CreateVersion7(), actor, null, TicketFactory.DefaultReportedAt.AddMinutes(5));

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Comments_keep_their_internal_visibility_flag()
    {
        var ticket = TicketFactory.Create();
        var author = Guid.CreateVersion7();

        ticket.AddComment("Customer visible update.", author, isInternal: false, TicketFactory.DefaultReportedAt);
        ticket.AddComment("Internal note for agents.", author, isInternal: true, TicketFactory.DefaultReportedAt);

        ticket.Comments.Should().HaveCount(2);
        ticket.Comments.Count(comment => comment.IsInternal).Should().Be(1);
    }

    [Fact]
    public void Ticket_number_is_trimmed_and_required()
    {
        var act = () => new Ticket(
            "   ",
            "Valid title",
            "Valid description",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TicketFactory.DefaultReportedAt);

        act.Should().Throw<DomainRuleViolationException>();
    }
}
