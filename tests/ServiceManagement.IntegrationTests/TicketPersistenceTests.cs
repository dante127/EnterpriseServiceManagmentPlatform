using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Persistence behaviour that can only be proven against a real SQL Server:
/// schema compatibility, the rowversion concurrency contract and the ticket
/// number sequence. Tests are skipped when no container runtime is available.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketPersistenceTests
{
    private readonly DatabaseFixture _fixture;

    public TicketPersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_ticket_round_trips_with_its_sla_deadlines()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();
        var scenario = await TestTicket.CreateAsync(_fixture, "TKT-900001");

        await using var context = _fixture.CreateContext();
        var persisted = await context.Tickets
            .Include(ticket => ticket.Comments)
            .SingleAsync(ticket => ticket.Id == scenario.TicketId);

        persisted.TicketNumber.Should().Be("TKT-900001");
        persisted.Status.Should().Be(TicketStatus.Assigned);
        persisted.FirstResponseDueAt.Should().Be(persisted.ReportedAt.AddMinutes(30));
        persisted.ResolutionDueAt.Should().Be(persisted.ReportedAt.AddMinutes(240));
        persisted.Comments.Should().ContainSingle();
        persisted.RowVersion.Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task Two_concurrent_updates_raise_a_concurrency_exception()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();
        var scenario = await TestTicket.CreateAsync(_fixture, "TKT-900002");

        await using var firstContext = _fixture.CreateContext();
        await using var secondContext = _fixture.CreateContext();

        var firstCopy = await firstContext.Tickets.SingleAsync(ticket => ticket.Id == scenario.TicketId);
        var secondCopy = await secondContext.Tickets.SingleAsync(ticket => ticket.Id == scenario.TicketId);

        // The first writer wins.
        firstCopy.MarkFirstResponse(scenario.ReportedAt.AddMinutes(5));
        await firstContext.SaveChangesAsync();

        // The second writer still holds the stale rowversion, so SQL Server rejects it.
        secondCopy.UpdateDetails(
            secondCopy.Title,
            "Updated by the losing writer.",
            secondCopy.CategoryId,
            secondCopy.PriorityId);

        var act = async () => await secondContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    // The sequence contract is covered end-to-end by TicketNumberGeneratorTests,
    // which exercises the real generator rather than re-implementing the SQL here.

    [SkippableFact]
    public async Task Soft_deleted_tickets_disappear_from_queries_but_stay_in_the_table()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();
        var scenario = await TestTicket.CreateAsync(_fixture, "TKT-900003");

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);
            ticket.IsDeleted = true;
            ticket.DeletedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var visible = await context.Tickets.AnyAsync(candidate => candidate.Id == scenario.TicketId);
            visible.Should().BeFalse();

            // The row itself is retained for auditing.
            var physical = await context.Tickets
                .IgnoreQueryFilters()
                .CountAsync(candidate => candidate.Id == scenario.TicketId);

            physical.Should().Be(1);
        }
    }

    [SkippableFact]
    public async Task A_duplicate_idempotency_key_is_rejected_by_the_unique_index()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();
        await TestTicket.CreateAsync(_fixture, "TKT-900004", idempotencyKey: "order-42");

        var act = async () => await TestTicket.CreateAsync(_fixture, "TKT-900005", idempotencyKey: "order-42");

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task Seeding_twice_does_not_duplicate_reference_data()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using (var context = _fixture.CreateContext())
        {
            await DatabaseSeeder.SeedAsync(context, new IdentityPasswordHasher(), DatabaseFixture.Clock);
        }

        await using (var context = _fixture.CreateContext())
        {
            var userCount = await context.Users.CountAsync();
            var permissionCount = await context.Permissions.CountAsync();
            var ticketCount = await context.Tickets.CountAsync();

            await DatabaseSeeder.SeedAsync(context, new IdentityPasswordHasher(), DatabaseFixture.Clock);

            (await context.Users.CountAsync()).Should().Be(userCount);
            (await context.Permissions.CountAsync()).Should().Be(permissionCount);
            (await context.Tickets.CountAsync()).Should().Be(ticketCount);
        }
    }

    /// <summary>Creates a realistic ticket through the real persistence path, using the seeded catalog.</summary>
    internal static class TestTicket
    {
        public static async Task<(Guid TicketId, DateTimeOffset ReportedAt)> CreateAsync(
            DatabaseFixture fixture,
            string ticketNumber,
            string? idempotencyKey = null)
        {
            await using var context = fixture.CreateContext();

            var requesterId = await context.Users
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

            var ticket = new Ticket(
                ticketNumber,
                "Laptop will not boot",
                "Integration scenario ticket created by the persistence test suite.",
                service.Id,
                service.CategoryId,
                priority.Id,
                requesterId,
                reportedAt);

            ticket.SetIdempotencyKey(idempotencyKey);
            ticket.ApplySlaPolicy(30, 240, 20);
            ticket.AssignToUser(agentId, requesterId, "Integration test assignment", reportedAt.AddMinutes(1));
            ticket.AddComment("First agent update.", agentId, isInternal: false, reportedAt.AddMinutes(2));

            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            return (ticket.Id, reportedAt);
        }
    }
}
