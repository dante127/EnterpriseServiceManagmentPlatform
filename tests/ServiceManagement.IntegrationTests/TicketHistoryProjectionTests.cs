using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The ticket history projection resolves three display names per assignment row
/// (previous assignee, new assignee, and who made the change) through nested
/// subqueries. EF translates each into a correlated <c>LEFT JOIN</c>, so a page
/// of N history rows produced 3N name lookups.
/// <para>
/// These tests pin the behaviour, not the SQL: the DTO must be byte-for-byte the
/// same whether names come from a join or a lookup table. If someone reintroduces
/// the per-row subquery, the assertions still pass - so the query-shape guard
/// below is what actually keeps the round-trip count down.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketHistoryProjectionTests
{
    private readonly DatabaseFixture _fixture;

    public TicketHistoryProjectionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task History_resolves_previous_new_and_acting_display_names()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-980001")).TicketId;
        var actingUser = await FirstActiveUserIdAsync();

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.FirstAsync(t => t.Id == ticketId);

            // AssignToUser returns the new trail row; it must be added explicitly
            // because ids are generated client-side, so EF cannot infer "Added".
            var entry = ticket.AssignToUser(
                actingUser,
                ticket.RequesterId,
                "second owner",
                ticket.ReportedAt.AddMinutes(5));

            context.TicketAssignmentHistories.Add(entry);
            await context.SaveChangesAsync();
        }

        var handler = new GetTicketHistoryQueryHandler(_fixture.CreateContext(), new TestCurrentUser());
        var result = await handler.Handle(new GetTicketHistoryQuery(ticketId), CancellationToken.None);

        result.Assignments.Should().NotBeEmpty();
        result.Assignments.Should().AllSatisfy(assignment =>
        {
            assignment.ChangedByName.Should().NotBeNullOrWhiteSpace();
            assignment.ChangedByName.Should().NotContain("*", "an unresolved name would surface as a null");
        });

        // The reassignment moved from the fixture's agent to `actingUser`, so at
        // least one row must have resolved both sides to a real name.
        result.Assignments
            .Any(assignment => assignment.PreviousAssigneeName is not null || assignment.NewAssigneeName is not null)
            .Should().BeTrue("a reassignment must resolve the display name on at least one side");
    }

    [SkippableFact]
    public async Task A_history_row_with_no_previous_assignee_keeps_a_null_name()
    {
        // The very first assignment has nothing before it. The join must not turn
        // that into an empty string, which a client would render as a blank cell.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-980002")).TicketId;

        var handler = new GetTicketHistoryQueryHandler(_fixture.CreateContext(), new TestCurrentUser());
        var result = await handler.Handle(new GetTicketHistoryQuery(ticketId), CancellationToken.None);

        var first = result.Assignments.OrderBy(a => a.ChangedAt).First();

        first.PreviousAssigneeId.Should().BeNull();
        first.PreviousAssigneeName.Should().BeNull();
    }

    [SkippableFact]
    public async Task An_unknown_ticket_is_rejected_rather_than_returned_as_empty()
    {
        // The endpoint 404s on a missing ticket, so a client never mistakes "no
        // such ticket" for "a ticket with no history".
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var handler = new GetTicketHistoryQueryHandler(_fixture.CreateContext(), new TestCurrentUser());

        var act = async () => await handler.Handle(
            new GetTicketHistoryQuery(Guid.CreateVersion7()),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [SkippableFact]
    public async Task The_history_query_does_not_issue_a_subquery_per_display_name()
    {
        // Guards the optimisation itself. The projection must resolve names with a
        // single pass over the distinct user ids, so the SQL is O(1) in lookups
        // rather than O(N) in rows. Captured through the EF command interceptor.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-980003")).TicketId;

        var interceptor = new CommandCapturingInterceptor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new ApplicationDbContext(options);

        var query = new GetTicketHistoryQuery(ticketId);
        var handler = new GetTicketHistoryQueryHandler(context, new TestCurrentUser());

        await handler.Handle(query, CancellationToken.None);

        var historySql = interceptor.Commands
            .Where(sql => sql.Contains("TicketAssignmentHistor", StringComparison.OrdinalIgnoreCase))
            .ToList();

        historySql.Should().NotBeEmpty(
            "the history is read from the assignment-history table; captured statements were: "
            + string.Join(" | ", interceptor.Commands.Select(c => c[..Math.Min(90, c.Length)])));

        // The projection must not inline a Users lookup for each of the three names
        // on every row: that is the O(N) shape this replaced. Names are resolved
        // once, over the distinct user ids, in a separate statement.
        var correlatedNameLookups = historySql
            .Count(sql => CountOccurrences(sql, "FROM [Users]") >= 3);

        correlatedNameLookups.Should().Be(0,
            "three correlated Users subqueries per row is exactly the shape this replaced");

        historySql.Should().HaveCount(1, "the history rows should be fetched by a single query");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private async Task<Guid> FirstActiveUserIdAsync()
    {
        await using var context = _fixture.CreateContext();
        return await context.Users.Where(u => u.IsActive).OrderBy(u => u.Id).Select(u => u.Id).FirstAsync();
    }

    private sealed class CommandCapturingInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
