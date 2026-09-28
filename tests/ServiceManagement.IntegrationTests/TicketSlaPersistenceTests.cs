using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// End-to-end proof that editing a ticket does not destroy its SLA deadlines.
/// The unit tests cover the aggregate in memory; this one proves the values
/// survive a real save/load round trip against SQL Server, which is where the
/// regression actually bit.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketSlaPersistenceTests
{
    private readonly DatabaseFixture _fixture;

    public TicketSlaPersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Editing_a_title_persists_the_sla_deadlines_unchanged()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();
        await EnsureCatalogAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-910001");

        DateTimeOffset? firstResponse;
        DateTimeOffset? resolution;

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);
            firstResponse = ticket.FirstResponseDueAt;
            resolution = ticket.ResolutionDueAt;

            // The historical bug: a title-only edit nulled both deadlines.
            ticket.UpdateDetails(
                "Laptop will not boot (revised wording)",
                "Still a black screen after the update.",
                ticket.CategoryId,
                ticket.PriorityId);

            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = await context.Tickets
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == scenario.TicketId);

            reloaded.FirstResponseDueAt.Should().Be(firstResponse);
            reloaded.ResolutionDueAt.Should().Be(resolution);
            reloaded.Title.Should().Be("Laptop will not boot (revised wording)");
        }
    }

    [SkippableFact]
    public async Task A_changed_priority_clears_the_deadlines_so_they_are_recomputed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();
        await EnsureCatalogAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-910002");

        // A real, different priority from the seeded catalog: the foreign key
        // constraint must stay satisfied while the deadlines are invalidated.
        Guid otherPriority;
        await using (var catalog = _fixture.CreateContext())
        {
            var current = await catalog.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.Id == scenario.TicketId)
                .Select(ticket => ticket.PriorityId)
                .FirstAsync();

            otherPriority = await catalog.ServicePriorities
                .Where(priority => priority.Id != current)
                .OrderBy(priority => priority.Level)
                .Select(priority => priority.Id)
                .FirstAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);

            ticket.UpdateDetails(
                ticket.Title,
                ticket.Description,
                ticket.CategoryId,
                otherPriority);

            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = await context.Tickets
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == scenario.TicketId);

            reloaded.PriorityId.Should().Be(otherPriority);
            reloaded.FirstResponseDueAt.Should().BeNull();
            reloaded.ResolutionDueAt.Should().BeNull();
        }
    }

    /// <summary>
    /// Seeds the reference catalog. A database supplied through
    /// <c>ESMP_TEST_SQL_CONNECTION</c> (CI, a fresh container) starts empty, and
    /// the seeder is idempotent, so calling it repeatedly is safe.
    /// </summary>
    private async Task EnsureCatalogAsync()
    {
        await using var context = _fixture.CreateContext();

        if (await context.Services.AnyAsync())
        {
            return;
        }

        await DatabaseSeeder.SeedAsync(context, new IdentityPasswordHasher(), DatabaseFixture.Clock);
    }
}
