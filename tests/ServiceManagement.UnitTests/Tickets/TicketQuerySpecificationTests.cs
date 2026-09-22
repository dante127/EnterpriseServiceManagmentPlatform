using FluentAssertions;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Constants;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.UnitTests.TestData;

namespace ServiceManagement.UnitTests.Tickets;

/// <summary>
/// The search specification is exercised without a database: it only composes
/// <see cref="IQueryable{T}"/> operators, so LINQ-to-objects behaves the same way
/// the SQL translation does.
/// </summary>
public class TicketQuerySpecificationTests
{
    private static readonly Guid ServiceA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ServiceB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PriorityCritical = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PriorityLow = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Requester = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void Filters_by_status()
    {
        var result = Apply(new TicketQuerySpecification { Status = TicketStatus.InProgress });

        result.Should().OnlyContain(ticket => ticket.Status == TicketStatus.InProgress);
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Filters_by_service_and_priority_together()
    {
        var result = Apply(new TicketQuerySpecification
        {
            ServiceId = ServiceA,
            PriorityId = PriorityCritical
        });

        result.Should().OnlyContain(ticket => ticket.ServiceId == ServiceA && ticket.PriorityId == PriorityCritical);
        result.Should().HaveCount(1);
    }

    [Fact]
    public void Filters_by_ticket_number_fragment()
    {
        var result = Apply(new TicketQuerySpecification { TicketNumber = "0002" });

        result.Should().ContainSingle().Which.TicketNumber.Should().Be("TKT-000002");
    }

    [Fact]
    public void Filters_by_reported_date_window()
    {
        var result = Apply(new TicketQuerySpecification
        {
            ReportedFrom = TicketFactory.DefaultReportedAt.AddDays(-3),
            ReportedTo = TicketFactory.DefaultReportedAt.AddDays(-1)
        });

        result.Should().OnlyContain(ticket => ticket.ReportedAt >= TicketFactory.DefaultReportedAt.AddDays(-3));
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Filters_by_sla_state()
    {
        var result = Apply(new TicketQuerySpecification { SlaStatus = SlaStatus.Breached });

        result.Should().OnlyContain(ticket => ticket.SlaStatus == SlaStatus.Breached);
    }

    [Fact]
    public void Soft_deleted_tickets_are_never_returned()
    {
        var deleted = TicketFactory.Create("TKT-000900");
        deleted.IsDeleted = true;

        var result = Apply(new TicketQuerySpecification(), deleted);

        result.Should().NotContain(ticket => ticket.TicketNumber == "TKT-000900");
    }

    [Fact]
    public void Default_order_is_newest_reported_first()
    {
        var result = Apply(new TicketQuerySpecification());

        result.Select(ticket => ticket.ReportedAt)
            .Should()
            .BeInDescendingOrder();
    }

    [Fact]
    public void Sort_direction_can_be_inverted()
    {
        var result = Apply(new TicketQuerySpecification { SortDescending = false });

        result.Select(ticket => ticket.ReportedAt).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Unknown_sort_keys_fall_back_to_the_default_order()
    {
        var result = Apply(new TicketQuerySpecification { SortBy = "not-a-column" });

        result.Select(ticket => ticket.ReportedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Ticket_access_policy_hides_internal_comments_from_requesters()
    {
        var requesterOnly = new[] { Permissions.TicketsRead, Permissions.TicketsComment };

        TicketAccessPolicy.CanViewInternalComments(requesterOnly).Should().BeFalse();
        TicketAccessPolicy.CanCreateInternalComment(isInternal: true, requesterOnly).Should().BeFalse();
        TicketAccessPolicy.CanCreateInternalComment(isInternal: false, requesterOnly).Should().BeTrue();
    }

    [Fact]
    public void Ticket_access_policy_allows_agents_to_write_internal_comments()
    {
        var agent = new[] { Permissions.TicketsRead, Permissions.TicketsCommentInternal };

        TicketAccessPolicy.CanViewInternalComments(agent).Should().BeTrue();
        TicketAccessPolicy.CanCreateInternalComment(isInternal: true, agent).Should().BeTrue();
    }

    [Fact]
    public void Terminal_tickets_are_reported_as_not_modifiable()
    {
        TicketAccessPolicy.CanModifyTicket(TicketStatus.Closed).Should().BeFalse();
        TicketAccessPolicy.CanModifyTicket(TicketStatus.Cancelled).Should().BeFalse();
        TicketAccessPolicy.CanModifyTicket(TicketStatus.InProgress).Should().BeTrue();
    }

    private static List<Ticket> Apply(TicketQuerySpecification specification, params Ticket[] extra)
    {
        var tickets = new List<Ticket>(BuildCorpus());
        tickets.AddRange(extra);

        return specification.Apply(tickets.AsQueryable()).ToList();
    }

    private static IEnumerable<Ticket> BuildCorpus()
    {
        var reportedAt = TicketFactory.DefaultReportedAt;

        var newTicket = Create("TKT-000001", reportedAt.AddDays(-5), ServiceA, PriorityCritical, TicketStatus.New, SlaStatus.WithinSla);

        var inProgress = Create("TKT-000002", reportedAt.AddDays(-2), ServiceB, PriorityLow, TicketStatus.New, SlaStatus.AtRisk);
        var actor = Guid.CreateVersion7();
        inProgress.AssignToUser(actor, actor, null, reportedAt.AddDays(-2).AddMinutes(5));
        inProgress.ChangeStatus(TicketStatus.InProgress, actor, reportedAt.AddDays(-2).AddMinutes(10));

        var breached = Create("TKT-000003", reportedAt.AddDays(-4), ServiceA, PriorityLow, TicketStatus.New, SlaStatus.Breached);
        breached.AssignToUser(actor, actor, null, reportedAt.AddDays(-4).AddMinutes(5));

        var resolved = Create("TKT-000004", reportedAt.AddDays(-3), ServiceB, PriorityCritical, TicketStatus.New, SlaStatus.Completed);
        resolved.AssignToUser(actor, actor, null, reportedAt.AddDays(-3).AddMinutes(5));
        resolved.ChangeStatus(TicketStatus.InProgress, actor, reportedAt.AddDays(-3).AddMinutes(10));
        resolved.ChangeStatus(TicketStatus.Resolved, actor, reportedAt.AddDays(-3).AddMinutes(20));

        return [newTicket, inProgress, breached, resolved];
    }

    private static Ticket Create(
        string ticketNumber,
        DateTimeOffset reportedAt,
        Guid serviceId,
        Guid priorityId,
        TicketStatus status,
        SlaStatus slaStatus)
    {
        var ticket = new Ticket(
            ticketNumber,
            $"Ticket {ticketNumber}",
            "Synthetic ticket used by the specification tests.",
            serviceId,
            categoryId: Guid.CreateVersion7(),
            priorityId,
            Requester,
            reportedAt);

        return new TicketStateSetter(ticket).Apply(status, slaStatus);
    }

    /// <summary>
    /// Small helper that materialises a specific state. It walks the real
    /// workflow instead of reaching into private setters.
    /// </summary>
    private sealed class TicketStateSetter
    {
        private readonly Ticket _ticket;
        private readonly Guid _actor = Guid.CreateVersion7();

        public TicketStateSetter(Ticket ticket)
        {
            _ticket = ticket;
        }

        public Ticket Apply(TicketStatus status, SlaStatus slaStatus)
        {
            if (status != TicketStatus.New)
            {
                if (_ticket.AssignedToId is null)
                {
                    _ticket.AssignToUser(_actor, _actor, null, _ticket.ReportedAt.AddMinutes(1));
                }

                if (status == TicketStatus.InProgress)
                {
                    _ticket.ChangeStatus(TicketStatus.InProgress, _actor, _ticket.ReportedAt.AddMinutes(2));
                }
            }

            if (status == TicketStatus.Resolved)
            {
                _ticket.ChangeStatus(TicketStatus.Resolved, _actor, _ticket.ReportedAt.AddMinutes(30));
            }

            if (status == TicketStatus.Closed)
            {
                _ticket.ChangeStatus(TicketStatus.Resolved, _actor, _ticket.ReportedAt.AddMinutes(30));
                _ticket.ChangeStatus(TicketStatus.Closed, _actor, _ticket.ReportedAt.AddMinutes(31));
            }

            if (slaStatus == SlaStatus.Breached)
            {
                // Freezes the state on the entity by evaluating far past the deadline.
                _ticket.ApplySlaPolicy(30, 60, 20);
                _ticket.RefreshSlaStatus(_ticket.ReportedAt.AddHours(5), 20);
            }
            else if (slaStatus == SlaStatus.AtRisk)
            {
                _ticket.ApplySlaPolicy(30, 240, 20);
                _ticket.RefreshSlaStatus(_ticket.ReportedAt.AddMinutes(200), 20);
            }
            else if (slaStatus != SlaStatus.Completed)
            {
                _ticket.ApplySlaPolicy(30, 240, 20);
            }

            return _ticket;
        }
    }
}
