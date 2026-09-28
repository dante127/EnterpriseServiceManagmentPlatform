using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Migrations;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// One-time repair for tickets damaged by the SLA invalidation bug, in which an
/// ordinary edit (one that did not change priority) nulled both deadlines. Because
/// <c>SlaMonitorWorker</c> only evaluates tickets with a
/// <c>ResolutionDueAt</c>, such a ticket silently fell out of monitoring and
/// could neither go AtRisk nor Breached.
/// <para>
/// The SQL is executed directly rather than through EF's migrator: the fixture
/// applies migrations when the database is provisioned, which happens before
/// these tests create their damaged rows. The statement comes from the migration
/// itself, so the SQL under test is exactly the SQL that ships.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SlaBackfillMigrationTests
{
    private readonly DatabaseFixture _fixture;

    public SlaBackfillMigrationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_damaged_ticket_regains_its_deadlines_from_its_active_policy()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var scenario = await CreateDamagedTicketAsync("TKT-960001");
        var policy = await ActivePolicyMinutesAsync(scenario.TicketId);

        await ApplyRepairAsync();

        var repaired = await LoadAsync(scenario.TicketId);

        // The targets must come from the ticket's own policy, not from a
        // hard-coded value: the fixture seeds a different policy per priority.
        repaired.FirstResponseDueAt.Should().Be(scenario.ReportedAt.AddMinutes(policy.FirstResponse));
        repaired.ResolutionDueAt.Should().Be(scenario.ReportedAt.AddMinutes(policy.Resolution));
    }

    [SkippableFact]
    public async Task An_untouched_ticket_keeps_the_deadlines_the_policy_original_gave_it()
    {
        // The repair recomputes from ReportedAt, so a second pass over an
        // already-correct row must be a no-op rather than a shift.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await PrepareAsync();
        var healthy = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-960002");
        var before = await LoadAsync(healthy.TicketId);

        await ApplyRepairAsync();

        var after = await LoadAsync(healthy.TicketId);

        after.FirstResponseDueAt.Should().Be(before.FirstResponseDueAt);
        after.ResolutionDueAt.Should().Be(before.ResolutionDueAt);
    }

    [SkippableFact]
    public async Task Running_the_repair_twice_changes_nothing()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var scenario = await CreateDamagedTicketAsync("TKT-960003");

        await ApplyRepairAsync();
        var first = await LoadAsync(scenario.TicketId);
        await ApplyRepairAsync();
        var second = await LoadAsync(scenario.TicketId);

        second.FirstResponseDueAt.Should().Be(first.FirstResponseDueAt, "the repair is idempotent");
        second.ResolutionDueAt.Should().Be(first.ResolutionDueAt);
    }

    [SkippableFact]
    public async Task A_partially_populated_ticket_is_left_alone()
    {
        // A half-populated row indicates a different fault. Overwriting it would
        // destroy information the repair was never meant to reconstruct.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var scenario = await CreateDamagedTicketAsync("TKT-960004");

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Tickets SET FirstResponseDueAt = DATEADD(minute, 7, ReportedAt) WHERE Id = {0}",
                scenario.TicketId);
        }

        var surviving = (await LoadAsync(scenario.TicketId)).FirstResponseDueAt;

        await ApplyRepairAsync();

        var after = await LoadAsync(scenario.TicketId);

        after.FirstResponseDueAt.Should().Be(surviving, "a half-populated row is out of scope");
        after.ResolutionDueAt.Should().BeNull("the repair must not fill in the missing half");
    }

    [SkippableFact]
    public async Task A_ticket_whose_priority_has_no_active_policy_still_gets_usable_targets()
    {
        // Leaving such a ticket without deadlines would repeat the very failure
        // the repair exists to undo: an unmeasurable ticket is invisible to the
        // monitor, so it can never go AtRisk or Breached.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var scenario = await CreateDamagedTicketAsync("TKT-960005");

        await using (var context = _fixture.CreateContext())
        {
            var priorityId = await PriorityIdAsync(scenario.TicketId);

            await context.SlaPolicies
                .Where(policy => policy.PriorityId == priorityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(policy => policy.IsActive, false));
        }

        await ApplyRepairAsync();

        var repaired = await LoadAsync(scenario.TicketId);

        repaired.FirstResponseDueAt.Should().NotBeNull();
        repaired.ResolutionDueAt.Should().NotBeNull();
        repaired.ResolutionDueAt.Should().BeAfter(repaired.FirstResponseDueAt!.Value);
    }

    [SkippableFact]
    public async Task The_repair_leaves_the_sla_state_for_the_monitor_to_decide()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var scenario = await CreateDamagedTicketAsync("TKT-960006");
        var before = await LoadAsync(scenario.TicketId);

        await ApplyRepairAsync();

        var after = await LoadAsync(scenario.TicketId);

        after.SlaStatus.Should().Be(before.SlaStatus, "the monitor owns the state; the repair only restores its inputs");
    }

    [SkippableFact]
    public async Task A_soft_deleted_ticket_is_not_resurrected_by_the_repair()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var scenario = await CreateDamagedTicketAsync("TKT-960007");

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Tickets SET IsDeleted = 1, DeletedAt = SYSUTCDATETIME() WHERE Id = {0}",
                scenario.TicketId);
        }

        await ApplyRepairAsync();

        await using (var verify = _fixture.CreateContext())
        {
            var physical = await verify.Tickets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(ticket => ticket.Id == scenario.TicketId)
                .Select(ticket => new { ticket.FirstResponseDueAt, ticket.ResolutionDueAt })
                .SingleAsync();

            physical.FirstResponseDueAt.Should().BeNull("a deleted ticket is out of scope");
            physical.ResolutionDueAt.Should().BeNull();
        }
    }

    private async Task PrepareAsync()
    {
        // These tests add their own policy rows, so the catalog is cleared as well
        // as the ticket data: the fixture clock is fixed, and a leftover policy
        // from a previous run would share a CreatedAt with the seeded rows and
        // make "newest wins" depend on the id tie-break.
        // ResetCatalogAsync re-seeds the catalog, so the seeded policies are active
        // again without any manual repair step.
        await _fixture.ResetCatalogAsync();
    }

    /// <summary>Creates a ticket then strips its deadlines, reproducing the bug.</summary>
    private async Task<(Guid TicketId, DateTimeOffset ReportedAt)> CreateDamagedTicketAsync(string ticketNumber)
    {
        await PrepareAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, ticketNumber);

        await using var context = _fixture.CreateContext();

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.Tickets SET FirstResponseDueAt = NULL, ResolutionDueAt = NULL WHERE Id = {0}",
            scenario.TicketId);

        return scenario;
    }

    private async Task<Guid> PriorityIdAsync(Guid ticketId)
    {
        await using var context = _fixture.CreateContext();

        return await context.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .Select(ticket => ticket.PriorityId)
            .FirstAsync();
    }

    /// <summary>
    /// The targets the repair should derive: the newest active policy for the
    /// ticket's priority, which is the same rule the SQL applies.
    /// </summary>
    private async Task<(int FirstResponse, int Resolution)> ActivePolicyMinutesAsync(Guid ticketId)
    {
        var priorityId = await PriorityIdAsync(ticketId);

        await using var context = _fixture.CreateContext();

        var policy = await context.SlaPolicies
            .AsNoTracking()
            .Where(candidate => candidate.PriorityId == priorityId && candidate.IsActive)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync();

        policy.Should().NotBeNull("the seeded catalog provides an active policy per priority");

        return (policy!.FirstResponseMinutes, policy.ResolutionMinutes);
    }

    private async Task<(DateTimeOffset? FirstResponseDueAt, DateTimeOffset? ResolutionDueAt, SlaStatusValue SlaStatus)>
        LoadAsync(Guid ticketId)
    {
        await using var context = _fixture.CreateContext();

        var row = await context.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .Select(ticket => new
            {
                ticket.FirstResponseDueAt,
                ticket.ResolutionDueAt,
                SlaStatus = (SlaStatusValue)ticket.SlaStatus
            })
            .FirstAsync();

        return (row.FirstResponseDueAt, row.ResolutionDueAt, row.SlaStatus);
    }

    private async Task ApplyRepairAsync()
    {
        await using var context = _fixture.CreateContext();

        foreach (var command in BackfillDamagedTicketSlaDeadlines.RepairSql())
        {
            await context.Database.ExecuteSqlRawAsync(command);
        }
    }

    private enum SlaStatusValue
    {
        WithinSla = 1,
        AtRisk = 2,
        Breached = 3,
        Completed = 4
    }
}
