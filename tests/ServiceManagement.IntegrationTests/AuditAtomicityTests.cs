using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Infrastructure.Auditing;
using ServiceManagement.Infrastructure.Notifications;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// A ticket change, its notifications and its audit record must commit together.
/// <para>
/// They used to be three independent transactions: the handler saved the ticket,
/// then the notification service saved again, then the audit service saved again.
/// A failure at any later point left the earlier work committed - most seriously,
/// a ticket could be modified with no audit record, which in an audit-grade system
/// is worse than the change never happening.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class AuditAtomicityTests
{
    private readonly DatabaseFixture _fixture;

    public AuditAtomicityTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task An_audit_entry_waits_for_the_callers_save()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateContext();
        var audit = new AuditService(context, new TestCurrentUser(), DatabaseFixture.Clock);

        await audit.RecordAsync(new AuditEntry(
            AuditActions.TicketCreated,
            "Ticket",
            Guid.CreateVersion7().ToString()));

        // Staging must not flush: the caller decides when the unit of work closes.
        (await context.AuditLogs.CountAsync())
            .Should().Be(0, "the audit service must not commit on its own");

        await context.SaveChangesAsync();

        (await context.AuditLogs.CountAsync()).Should().Be(1);
    }

    [SkippableFact]
    public async Task Notifications_wait_for_the_callers_save_too()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateContext();
        var notifications = new DatabaseNotificationService(
            context,
            TestLoggers.For<DatabaseNotificationService>());

        // A real user: the notification table holds a foreign key to Users, so a
        // random identifier would fail the save for the wrong reason.
        var userId = await context.Users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .FirstAsync();

        await notifications.NotifyAsync(new NotificationMessage(
            userId,
            NotificationType.TicketAssigned,
            "Title",
            "Message"));

        (await context.Notifications.CountAsync())
            .Should().Be(0, "the notification service must not commit on its own");

        await context.SaveChangesAsync();

        (await context.Notifications.CountAsync()).Should().Be(1);
    }

    [SkippableFact]
    public async Task A_rolled_back_unit_of_work_leaves_no_audit_record_behind()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var entityId = Guid.CreateVersion7().ToString();

        await using (var context = _fixture.CreateContext())
        {
            var audit = new AuditService(context, new TestCurrentUser(), DatabaseFixture.Clock);

            await using var transaction = await context.Database.BeginTransactionAsync();

            await audit.RecordAsync(new AuditEntry(
                AuditActions.TicketCreated,
                "Ticket",
                entityId));

            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verify = _fixture.CreateContext();

        // The whole point: an aborted change must not leave an orphan audit row.
        (await verify.AuditLogs.CountAsync(log => log.EntityId == entityId))
            .Should().Be(0, "a rolled-back change must not leave an audit record");
    }

    [SkippableFact]
    public async Task A_notification_and_its_audit_record_commit_together_or_not_at_all()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateContext();
        var audit = new AuditService(context, new TestCurrentUser(), DatabaseFixture.Clock);
        var notifications = new DatabaseNotificationService(
            context,
            TestLoggers.For<DatabaseNotificationService>());

        var entityId = Guid.CreateVersion7().ToString();
        var userId = await context.Users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .FirstAsync();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await notifications.NotifyAsync(new NotificationMessage(
                userId,
                NotificationType.TicketAssigned,
                "Title",
                "Message"));

            await audit.RecordAsync(new AuditEntry(
                AuditActions.TicketCreated,
                "Ticket",
                entityId));

            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verify = _fixture.CreateContext();

        (await verify.AuditLogs.CountAsync(log => log.EntityId == entityId))
            .Should().Be(0);
        (await verify.Notifications.CountAsync())
            .Should().Be(0, "neither the trail nor the notification survives a rollback");
    }

    [SkippableFact]
    public async Task A_committed_unit_of_work_keeps_the_audit_record_with_the_change()
    {
        // The positive case: staging must not lose the record either.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var entityId = Guid.CreateVersion7().ToString();

        await using (var context = _fixture.CreateContext())
        {
            var audit = new AuditService(context, new TestCurrentUser(), DatabaseFixture.Clock);

            await using var transaction = await context.Database.BeginTransactionAsync();

            await audit.RecordAsync(new AuditEntry(
                AuditActions.TicketCreated,
                "Ticket",
                entityId,
                NewValues: AuditJson.Serialize(new { Title = "probe" })));

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = _fixture.CreateContext();

        (await verify.AuditLogs.CountAsync(log => log.EntityId == entityId))
            .Should().Be(1, "staging must not drop the record when the unit of work commits");
    }
}
