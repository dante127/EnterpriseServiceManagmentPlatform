using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Tickets;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.Auditing;
using ServiceManagement.Infrastructure.Notifications;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The assignment trail: every assignment change must leave a TicketAssignmentHistory
/// row. The handler reuses one variable for the row it persists, so a request that
/// assigned a user AND a team in one call silently kept only the second row - and
/// the user half of the reassignment never existed as far as history was concerned.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class TicketAssignmentTrailTests
{
    private readonly DatabaseFixture _fixture;

    public TicketAssignmentTrailTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Assigning_a_user_and_a_team_in_one_call_records_both_trail_rows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (ticketId, _) = await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-960001");

        Guid agentId;
        Guid managerId;
        Guid teamId;

        await using (var context = _fixture.CreateContext())
        {
            agentId = await context.Users
                .Where(user => user.Email == DatabaseSeeder.AgentEmail)
                .Select(user => user.Id)
                .FirstAsync();
            managerId = await context.Users
                .Where(user => user.Email == DatabaseSeeder.ManagerEmail)
                .Select(user => user.Id)
                .FirstAsync();
            teamId = await context.Teams.Select(team => team.Id).FirstAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            // The actor must be allowed to change the ticket, so the agent (who
            // made the original assignment) plays that part too.
            var currentUser = new TestCurrentUser { UserId = agentId };

            var handler = new AssignTicketCommandHandler(
                context,
                new DatabaseNotificationService(context, TestLoggers.For<DatabaseNotificationService>()),
                new AuditService(context, currentUser, DatabaseFixture.Clock),
                currentUser,
                DatabaseFixture.Clock);

            await handler.Handle(
                new AssignTicketCommand(ticketId, managerId, teamId, "Escalate to the service desk", null),
                CancellationToken.None);
        }

        await using var verify = _fixture.CreateContext();
        var history = await verify.TicketAssignmentHistories.AsNoTracking()
            .Where(entry => entry.TicketId == ticketId)
            .ToListAsync();

        history.Should().Contain(entry =>
                entry.NewAssigneeId == managerId
                && entry.PreviousAssigneeId == agentId
                && entry.NewTeamId == null,
            "the user half of the change must be trailed, not overwritten by the team half");
        history.Should().Contain(entry => entry.NewTeamId == teamId, "the team assignment must be trailed");

        var ticket = await verify.Tickets.AsNoTracking()
            .Where(candidate => candidate.Id == ticketId)
            .Select(candidate => new { candidate.AssignedToId, candidate.AssignedTeamId })
            .SingleAsync();

        ticket.AssignedTeamId.Should().NotBeNull("the team assignment must still be applied");
    }
}
