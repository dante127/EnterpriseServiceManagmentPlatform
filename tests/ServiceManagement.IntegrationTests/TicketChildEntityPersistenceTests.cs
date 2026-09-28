using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Regression coverage for the false concurrency conflict that made assignment
/// and commenting fail against a real database.
/// <para>
/// Identifiers are generated in <c>BaseEntity</c>, so EF cannot distinguish a
/// brand-new child row from a persisted one by its key. A comment or assignment
/// trail row added to an already-loaded ticket was therefore discovered as
/// <c>Modified</c>, the UPDATE matched zero rows, and the caller received HTTP
/// 409 "the record was modified by another user" - a conflict nobody caused.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketChildEntityPersistenceTests
{
    private readonly DatabaseFixture _fixture;

    public TicketChildEntityPersistenceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_ticket_trail_row_added_after_persistence_is_inserted()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-930001");

        Guid trailId;
        await using (var context = _fixture.CreateContext())
        {
            await SeedAsync(context);

            var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);
            var team = await context.Teams.FirstAsync();

            var entry = ticket.AssignToTeam(team.Id, ticket.RequesterId, "Isolation test", scenario.ReportedAt.AddMinutes(3));

            trailId = entry.Id;
            context.TicketAssignmentHistories.Add(entry);

            var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            exception.Should().BeNull("assigning to an existing ticket must not conflict: {0}", exception?.ToString());
        }

        await using (var verify = _fixture.CreateContext())
        {
            var stored = await verify.TicketAssignmentHistories
                .AsNoTracking()
                .AnyAsync(history => history.Id == trailId);

            stored.Should().BeTrue("the assignment trail must be persisted, not silently dropped");
        }
    }

    [SkippableFact]
    public async Task A_comment_added_to_an_existing_ticket_is_inserted()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-930005");

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);
            var author = await context.Users.OrderBy(user => user.Id).FirstAsync();

            var comment = ticket.AddComment(
                "A note added after the ticket was already persisted.",
                author.Id,
                isInternal: false,
                DateTimeOffset.UtcNow);

            context.TicketComments.Add(comment);

            var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync());
            exception.Should().BeNull("adding a comment must insert, not conflict: {0}", exception?.ToString());
        }

        await using var verify = _fixture.CreateContext();
        var stored = await verify.TicketComments.CountAsync(comment => comment.TicketId == scenario.TicketId);
        stored.Should().Be(2, "the seeded comment plus the newly added one");
    }

    [SkippableFact]
    public async Task A_plain_ticket_edit_still_saves_without_conflict()
    {
        // Baseline: a ticket UPDATE with only scalar changes must keep working, so
        // the fix above does not mask a genuine concurrency problem.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var scenario = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-930003");

        await using var context = _fixture.CreateContext();
        var ticket = await context.Tickets.SingleAsync(candidate => candidate.Id == scenario.TicketId);

        ticket.UpdateDetails(
            "Renamed without any assignment.",
            ticket.Description,
            ticket.CategoryId,
            ticket.PriorityId);

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync());
        exception.Should().BeNull("a plain title edit must not conflict: {0}", exception?.ToString());
    }

    [SkippableFact]
    public async Task An_existence_check_leaves_the_change_tracker_untouched()
    {
        // The handlers verify assignees and teams with AnyAsync before mutating.
        // A probe that marked something as modified would poison the later save.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateContext();
        await SeedAsync(context);

        var team = await context.Teams.FirstAsync();

        (await context.Teams.AnyAsync(candidate => candidate.Id == team.Id)).Should().BeTrue();

        var dirty = context.ChangeTracker
            .Entries<Team>()
            .Where(entry => entry.State != EntityState.Unchanged)
            .ToList();

        dirty.Should().BeEmpty("a pure existence check must not mark anything as modified");
    }

    private static async Task SeedAsync(ApplicationDbContext context)
        => await DatabaseSeeder.SeedAsync(context, new IdentityPasswordHasher(), DatabaseFixture.Clock);
}
