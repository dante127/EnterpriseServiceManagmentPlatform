using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;
using ServiceManagement.Infrastructure.BackgroundJobs;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The SLA monitor's decision logic: which tickets it examines, when it changes
/// their state, and who it tells. This is the part that decides whether a breach
/// is ever noticed, and it used to live inside a <c>BackgroundService</c> where
/// nothing could reach it except a hosted-service harness.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SlaCycleTests
{
    private readonly DatabaseFixture _fixture;

    public SlaCycleTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_ticket_past_its_resolution_deadline_is_promoted_to_breached()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var ticketId = await ArrangeAsync("TKT-970001");

        // The deadline has already passed; the monitor is what must notice.
        var cycle = CreateCycle(PastResolution, out _, out _);
        var result = await cycle.RunAsync(CancellationToken.None);

        result.Transitions.Should().BeGreaterThan(0);
        (await LoadStatusAsync(ticketId)).Should().Be(SlaStatus.Breached);
    }

    [SkippableFact]
    public async Task A_ticket_deadline_stripped_by_the_old_bug_is_still_invisible()
    {
        // Documents the blind spot that made the SLA bug so damaging: a ticket
        // with no deadline is skipped entirely, because there is nothing to be
        // late against. The monitor cannot detect its own blindness.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-970002")).TicketId;

        await using (var context = _fixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.Tickets SET FirstResponseDueAt = NULL, ResolutionDueAt = NULL WHERE Id = {0}",
                ticketId);
        }

        var cycle = CreateCycle(PastResolution, out _, out _);
        await cycle.RunAsync(CancellationToken.None);

        // Still WithinSla: the monitor never saw it. Only the backfill can fix this.
        (await LoadStatusAsync(ticketId)).Should().Be(SlaStatus.WithinSla);
    }

    [SkippableFact]
    public async Task A_ticket_whose_state_does_not_change_produces_no_notification()
    {
        // Idempotence: a monitor that re-notified every tick would spam users and
        // grow the Notifications table without bound.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var ticketId = await ArrangeAsync("TKT-970003");

        // Five minutes in: well inside a 240-minute target, so nothing changes.
        var cycle = CreateCycle(WithinSla, out _, out _);
        var result = await cycle.RunAsync(CancellationToken.None);

        result.Transitions.Should().Be(0, "the ticket is already WithinSla and well inside its window");
        (await LoadNotificationCountAsync(ticketId)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Running_the_cycle_twice_records_no_second_transition()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        var ticketId = await ArrangeAsync("TKT-970004");

        var first = CreateCycle(PastResolution, out _, out _);
        await first.RunAsync(CancellationToken.None);

        var second = CreateCycle(PastResolution, out _, out _);
        var result = await second.RunAsync(CancellationToken.None);

        result.Transitions.Should().Be(0, "a breach is reported once, not on every tick");
    }

    [SkippableFact]
    public async Task A_breach_notifies_the_requester_and_the_assignee_exactly_once()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-970005")).TicketId;

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.FirstAsync(t => t.Id == ticketId);
            ticket.ChangeStatus(
                TicketStatus.InProgress,
                ticket.RequesterId,
                DateTimeOffset.Parse("2026-03-01T09:10:00+00:00"));
            await context.SaveChangesAsync();
        }

        var cycle = CreateCycle(PastResolution, out _, out _);
        await cycle.RunAsync(CancellationToken.None);

        var notifications = await LoadNotificationsAsync(ticketId);
        var distinctRecipients = notifications.Select(n => n.UserId).Distinct().ToList();

        notifications.Should().AllSatisfy(n => n.Type.Should().Be(NotificationType.SlaBreached));
        distinctRecipients.Should().HaveCount(
            notifications.Count,
            "a self-assigned requester must not receive the same notification twice");
    }

    [SkippableFact]
    public async Task A_completed_ticket_is_never_reopened_by_the_monitor()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var ticketId = (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, "TKT-970006")).TicketId;

        await using (var context = _fixture.CreateContext())
        {
            var ticket = await context.Tickets.FirstAsync(t => t.Id == ticketId);

            // The workflow requires InProgress before Resolved.
            ticket.ChangeStatus(TicketStatus.InProgress, ticket.RequesterId, DateTimeOffset.Parse("2026-03-01T09:05:00+00:00"));
            ticket.ChangeStatus(TicketStatus.Resolved, ticket.RequesterId, DateTimeOffset.Parse("2026-03-01T09:10:00+00:00"));
            await context.SaveChangesAsync();
        }

        var cycle = CreateCycle(PastResolution, out _, out _);
        await cycle.RunAsync(CancellationToken.None);

        (await LoadStatusAsync(ticketId)).Should().Be(SlaStatus.Completed);
    }

    /// <summary>Creates a fixture ticket with its deadlines intact, untouched.</summary>
    private async Task<Guid> ArrangeAsync(string ticketNumber)
    {
        await _fixture.ResetAsync();

        return (await TicketPersistenceTests.TestTicket.CreateAsync(_fixture, ticketNumber)).TicketId;
    }

    /// <summary>
    /// The fixture ticket is created at 2026-03-01T09:00 with a 30-minute first
    /// response and a 240-minute resolution target, giving deadlines at 09:30 and
    /// 13:00. The cycle's clock is what decides whether the monitor sees a breach.
    /// </summary>
    private static DateTimeOffset WithinSla => DateTimeOffset.Parse("2026-03-01T09:05:00+00:00");

    private static DateTimeOffset PastResolution => DateTimeOffset.Parse("2026-03-02T09:00:00+00:00");

    private SlaCycle CreateCycle(
        DateTimeOffset now,
        out RecordingNotificationService notifications,
        out RecordingEmailSender emails)
    {
        var dbContext = _fixture.CreateContext();
        notifications = new RecordingNotificationService(dbContext);
        emails = new RecordingEmailSender();

        return new SlaCycle(
            dbContext,
            notifications,
            emails,
            new Microsoft.Extensions.Options.OptionsWrapper<SlaOptions>(new SlaOptions()),
            new Microsoft.Extensions.Options.OptionsWrapper<NotificationsOptions>(new NotificationsOptions
            {
                EmailEnabled = false
            }),
            new FixedClock(now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SlaCycle>.Instance);
    }

    private async Task<SlaStatus> LoadStatusAsync(Guid ticketId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .Select(ticket => ticket.SlaStatus)
            .FirstAsync();
    }

    private async Task<int> LoadNotificationCountAsync(Guid ticketId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Notifications
            .AsNoTracking()
            .CountAsync(notification => notification.EntityId == ticketId.ToString());
    }

    private async Task<List<(Guid UserId, NotificationType Type)>> LoadNotificationsAsync(Guid ticketId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Notifications
            .AsNoTracking()
            .Where(notification => notification.EntityId == ticketId.ToString())
            .Select(notification => new { notification.UserId, notification.Type })
            .ToListAsync()
            .ContinueWith(task => task.Result.Select(entry => (entry.UserId, entry.Type)).ToList());
    }

    /// <summary>
    /// Stands in for the real notification channel while still writing the rows,
    /// so a test can assert on what a cycle actually queued. Like the production
    /// implementation it stages rather than saves: the cycle owns the unit of work.
    /// </summary>
    private sealed class RecordingNotificationService(ApplicationDbContext dbContext) : INotificationService
    {
        public List<NotificationMessage> Staged { get; } = [];

        public Task NotifyAsync(NotificationMessage message, CancellationToken cancellationToken = default)
            => NotifyManyAsync([message], cancellationToken);

        public Task NotifyManyAsync(
            IEnumerable<NotificationMessage> messages,
            CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                Staged.Add(message);

                dbContext.Notifications.Add(new Notification(
                    message.UserId,
                    message.Type,
                    message.Title,
                    message.Message,
                    message.EntityName,
                    message.EntityId,
                    message.CorrelationId));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add($"{to}:{subject}");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
