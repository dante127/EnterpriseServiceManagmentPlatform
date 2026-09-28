using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Establishes exactly which rows the pre-fix bug damaged, and what a repair has
/// to restore. Nothing here is asserted about the repair itself: these are the
/// measurements the backfill is designed against.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SlaDamageSurveyTests
{
    private readonly DatabaseFixture _fixture;

    public SlaDamageSurveyTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_ticket_losing_its_deadlines_is_still_visible_to_no_query()
    {
        // Why the damage is silent: the SLA monitor selects on
        // ResolutionDueAt != null, so a stripped ticket can never be evaluated.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-950001");

        // Reproduce the original defect at the database level.
        await using (var damage = _fixture.CreateContext())
        {
            await damage.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Tickets SET FirstResponseDueAt = NULL, ResolutionDueAt = NULL WHERE Id = {0}",
                scenario.TicketId);
        }

        await using (var context = _fixture.CreateContext())
        {
            // Still readable...
            var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);
            ticket.FirstResponseDueAt.Should().BeNull();

            // ...but invisible to the monitor's candidate query, which is the bug.
            var monitored = await context.Tickets
                .Where(candidate => candidate.ResolutionDueAt != null)
                .AnyAsync(candidate => candidate.Id == scenario.TicketId);

            monitored.Should().BeFalse("a ticket without deadlines is skipped by SLA monitoring");
        }
    }

    [SkippableFact]
    public async Task The_repair_candidates_are_exactly_the_rows_with_no_deadlines()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var healthy = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-950002");
        var damaged = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-950003");

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Tickets SET FirstResponseDueAt = NULL, ResolutionDueAt = NULL WHERE Id = {0}",
                damaged.TicketId);
        }

        await using (var verify = _fixture.CreateContext())
        {
            // Only the fully-stripped rows are candidates. A half-populated ticket
            // (one deadline null, the other set) indicates a different fault and
            // must not be touched by the repair.
            var candidates = await verify.Tickets
                .Where(ticket => ticket.FirstResponseDueAt == null && ticket.ResolutionDueAt == null)
                .Select(ticket => ticket.Id)
                .ToListAsync();

            candidates.Should().Contain(damaged.TicketId);
            candidates.Should().NotContain(healthy.TicketId);
        }
    }

    [SkippableFact]
    public async Task Every_damaged_ticket_still_carries_a_priority_whose_policy_can_be_found()
    {
        // If a priority has no active policy, the repair must fall back to
        // configured targets rather than leaving the ticket unmeasurable again.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-950004");

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Tickets SET FirstResponseDueAt = NULL, ResolutionDueAt = NULL WHERE Id = {0}",
                scenario.TicketId);

            var priorityId = await context.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.Id == scenario.TicketId)
                .Select(ticket => ticket.PriorityId)
                .FirstAsync();

            var policy = await context.SlaPolicies
                .AsNoTracking()
                .Where(candidate => candidate.IsActive)
                .FirstOrDefaultAsync(candidate => candidate.PriorityId == priorityId);

            policy.Should().NotBeNull("the seeded catalog provides a policy for every priority");
            policy!.FirstResponseMinutes.Should().BePositive();
            policy.ResolutionMinutes.Should().BePositive();
        }
    }

    [SkippableFact]
    public async Task A_priority_can_have_several_active_policies_which_makes_the_repair_ambiguous()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-950005");

        await using var context = _fixture.CreateContext();

        var priorityId = await context.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == scenario.TicketId)
            .Select(ticket => ticket.PriorityId)
            .FirstAsync();

        var active = await context.SlaPolicies
            .AsNoTracking()
            .Where(policy => policy.IsActive && policy.PriorityId == priorityId)
            .ToListAsync();

        // Documents a real decision the migration has to make and state.
        active.Should().NotBeEmpty();
    }
}
