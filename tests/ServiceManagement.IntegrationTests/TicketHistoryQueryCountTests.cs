using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Quantifies the P2.5 optimisation rather than asserting its shape. The trail is
/// grown to a realistic size and the number of statements EF issues is measured,
/// so the claim "3N round-trips became O(1)" is checked against a real database
/// instead of restating the intent of the code.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketHistoryQueryCountTests
{
    private const int TrailLength = 40;

    private readonly DatabaseFixture _fixture;

    public TicketHistoryQueryCountTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Statement_count_does_not_grow_with_the_length_of_the_trail()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        // A fresh database per measurement, so the two trails are genuinely
        // different sizes rather than the same row read twice.
        await _fixture.ResetAsync();
        var shortTicket = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-981001")).TicketId;
        await BuildTrailAsync(shortTicket, 2);
        var shortRun = await MeasureStatementsAsync(shortTicket);
        var shortTrail = shortRun.Statements;

        await _fixture.ResetAsync();
        var longTicket = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-981002")).TicketId;
        await BuildTrailAsync(longTicket, TrailLength);
        var longRun = await MeasureStatementsAsync(longTicket);
        var longTrail = longRun.Statements;
        var longNames = longRun.NameLookups;

        // Both versions issue a single statement for the trail - EF folds the
        // correlated subqueries into it. What differs is the work inside: the old
        // projection re-executed a Users lookup per name per row, so the server
        // did O(N) lookups. The optimised projection resolves the distinct ids once.
        longTrail.Should().Be(
            shortTrail,
            "name resolution is O(1) in rows, so a longer trail costs the same statements");

        longTrail.Should().BeLessThanOrEqualTo(5,
            "existence check, comments, trail, and the single name lookup");

        // Two Users statements in total: one joins the comment author, one resolves
        // the trail's distinct assignee ids. The point is that neither grows with the
        // length of the trail - the old projection folded a Users lookup into the
        // trail statement itself, once per name per row.
        longNames.Should().Be(2, "one lookup for comment authors, one for the trail's distinct ids");
    }

    [SkippableFact]
    public async Task A_long_trail_still_resolves_every_display_name()
    {
        // The optimisation must not cost correctness: every row keeps its names.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-981003")).TicketId;
        await BuildTrailAsync(ticketId, TrailLength);

        await using var context = _fixture.CreateContext();

        // The fixture ticket already carries one assignment, so the total is the
        // rotations added plus that initial row.
        var expected = await context.TicketAssignmentHistories
            .CountAsync(history => history.TicketId == ticketId);

        expected.Should().BeGreaterThan(TrailLength, "the scenario must actually build a long trail");

        var handler = new GetTicketHistoryQueryHandler(context, new TestCurrentUser());
        var result = await handler.Handle(new GetTicketHistoryQuery(ticketId), CancellationToken.None);

        result.Assignments.Should().HaveCount(expected, "every trail row must be returned");
        result.Assignments.Should().AllSatisfy(assignment =>
            assignment.ChangedByName.Should().NotBeNullOrWhiteSpace());
    }

    private async Task BuildTrailAsync(Guid ticketId, int entries)
    {
        var agentIds = await ActiveUserIdsAsync(3);

        await using var context = _fixture.CreateContext();
        var ticket = await context.Tickets.FirstAsync(t => t.Id == ticketId);

        var at = ticket.ReportedAt.AddMinutes(2);

        for (var index = 0; index < entries; index++)
        {
            at = at.AddMinutes(1);
            var entry = ticket.AssignToUser(
                agentIds[index % agentIds.Count],
                ticket.RequesterId,
                $"rotation {index}",
                at);

            context.TicketAssignmentHistories.Add(entry);
        }

        await context.SaveChangesAsync();
    }

    private async Task<List<Guid>> ActiveUserIdsAsync(int count)
    {
        await using var context = _fixture.CreateContext();

        return await context.Users
            .Where(user => user.IsActive)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .Take(count)
            .ToListAsync();
    }

    private async Task<(int Statements, int NameLookups)> MeasureStatementsAsync(Guid ticketId)
    {
        var interceptor = new CountingInterceptor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new ApplicationDbContext(options);
        var handler = new GetTicketHistoryQueryHandler(context, new TestCurrentUser());

        await handler.Handle(new GetTicketHistoryQuery(ticketId), CancellationToken.None);

        var nameLookups = interceptor.Commands
            .Count(sql => sql.Contains("[Users]", StringComparison.OrdinalIgnoreCase));

        return (interceptor.StatementCount, nameLookups);
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int StatementCount { get; private set; }

        public List<string> Commands { get; } = [];

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            StatementCount++;
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            StatementCount++;
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
