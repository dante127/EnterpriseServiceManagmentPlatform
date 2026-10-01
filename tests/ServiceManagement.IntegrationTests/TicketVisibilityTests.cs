using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Constants;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Ticket visibility: staff see the whole queue; requesters see only the tickets
/// they raised. The scoped read used to be missing entirely, so any holder of
/// tickets.read could enumerate the organization's entire ticket list, and a
/// scoped caller filtering by someone else's id got that someone else's tickets.
/// <para>
/// A ticket a scoped caller does not own answers 404, not 403: a 403 would
/// reveal that the id exists.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketVisibilityTests
{
    private readonly DatabaseFixture _fixture;

    public TicketVisibilityTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_requester_sees_only_their_own_tickets_in_search()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (ritaId, agentId, ownTicketId, foreignTicketId) = await ArrangeAsync();

        var rita = ScopedCaller(ritaId);
        var staff = StaffCaller();

        // The seeder also raises demo tickets on Rita's behalf, so the assertion
        // is on the scope itself, not on absolute counts.
        var own = await new SearchTicketsQueryHandler(_fixture.CreateContext(), rita)
            .Handle(UnfilteredSearch(), CancellationToken.None);

        own.Items.Should().Contain(item => item.Id == ownTicketId);
        own.Items.Should().NotContain(item => item.Id == foreignTicketId,
            "a requester sees only their own tickets");

        var everything = await new SearchTicketsQueryHandler(_fixture.CreateContext(), staff)
            .Handle(UnfilteredSearch(), CancellationToken.None);

        everything.Items.Should().Contain(item => item.Id == ownTicketId);
        everything.Items.Should().Contain(item => item.Id == foreignTicketId,
            "staff visibility covers the whole queue");

        // A scoped caller filtering by someone else's id is forced back to their
        // own scope instead of getting the foreign tickets.
        var attemptedEscalation = await new SearchTicketsQueryHandler(_fixture.CreateContext(), rita)
            .Handle(UnfilteredSearch() with { RequesterId = agentId }, CancellationToken.None);

        attemptedEscalation.Items.Should().NotContain(item => item.Id == foreignTicketId);
    }

    [SkippableFact]
    public async Task A_foreign_ticket_answers_404_for_a_scoped_caller()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (ritaId, _, ownTicketId, foreignTicketId) = await ArrangeAsync();
        var rita = ScopedCaller(ritaId);

        var own = await new GetTicketByIdQueryHandler(_fixture.CreateContext(), rita)
            .Handle(new GetTicketByIdQuery(ownTicketId), CancellationToken.None);

        own.Id.Should().Be(ownTicketId);

        var act = async () => await new GetTicketByIdQueryHandler(_fixture.CreateContext(), rita)
            .Handle(new GetTicketByIdQuery(foreignTicketId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>("a 404 keeps the ticket's existence private");
    }

    [SkippableFact]
    public async Task Ticket_history_is_scoped_the_same_way_as_detail()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (ritaId, _, ownTicketId, foreignTicketId) = await ArrangeAsync();
        var rita = ScopedCaller(ritaId);

        var own = await new GetTicketHistoryQueryHandler(_fixture.CreateContext(), rita)
            .Handle(new GetTicketHistoryQuery(ownTicketId), CancellationToken.None);

        own.TicketId.Should().Be(ownTicketId);

        var act = async () => await new GetTicketHistoryQueryHandler(_fixture.CreateContext(), rita)
            .Handle(new GetTicketHistoryQuery(foreignTicketId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private static SearchTicketsQuery UnfilteredSearch() =>
        new(TicketNumber: null,
            Title: null,
            Status: null,
            PriorityId: null,
            CategoryId: null,
            ServiceId: null,
            RequesterId: null,
            AssignedToId: null,
            AssignedTeamId: null,
            SlaStatus: null,
            ReportedFrom: null,
            ReportedTo: null);

    private static ICurrentUserService ScopedCaller(Guid userId) => new TestCurrentUser
    {
        UserId = userId,
        Permissions = [Permissions.TicketsRead, Permissions.TicketsCreate, Permissions.TicketsComment]
    };

    private static ICurrentUserService StaffCaller() => new TestCurrentUser
    {
        UserId = Guid.CreateVersion7(),
        Permissions = [Permissions.TicketsRead, Permissions.TicketsUpdate]
    };

    /// <summary>
    /// One ticket raised by the seeded requester (via the shared TestTicket
    /// helper) and one raised by the agent directly, so both sides of the scope
    /// exist in a known arrangement.
    /// </summary>
    private async Task<(Guid RitaId, Guid AgentId, Guid OwnTicketId, Guid ForeignTicketId)> ArrangeAsync()
    {
        var ownTicketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-950001")).TicketId;

        await using var context = _fixture.CreateContext();

        var ritaId = await context.Users
            .Where(user => user.Email == DatabaseSeeder.RequesterEmail)
            .Select(user => user.Id)
            .FirstAsync();
        var agentId = await context.Users
            .Where(user => user.Email == DatabaseSeeder.AgentEmail)
            .Select(user => user.Id)
            .FirstAsync();

        var service = await context.Services
            .Include(candidate => candidate.Category)
            .FirstAsync();
        var priority = await context.ServicePriorities
            .OrderBy(candidate => candidate.Level)
            .FirstAsync();

        var reportedAt = DatabaseFixture.Clock.UtcNow;
        var foreign = new Ticket(
            "TKT-950002",
            "Raised by the agent",
            "A ticket whose requester is not the scoped caller.",
            service.Id,
            service.CategoryId,
            priority.Id,
            agentId,
            reportedAt);

        foreign.ApplySlaPolicy(30, 240, 20);

        context.Tickets.Add(foreign);
        await context.SaveChangesAsync();

        return (ritaId, agentId, ownTicketId, foreign.Id);
    }
}
