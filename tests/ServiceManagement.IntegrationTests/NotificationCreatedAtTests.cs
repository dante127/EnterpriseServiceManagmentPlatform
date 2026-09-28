using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Notifications;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Interceptors;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The notification DTO reports <c>CreatedAt</c>, which is written by
/// <c>AuditableEntityInterceptor</c> rather than by the entity constructor.
/// <para>
/// The bare test context deliberately omits that interceptor, so audit columns
/// stay at their default. These tests therefore build a context the way production
/// does, and assert the projection reads the stored value - which is what an
/// in-memory adapt could not guarantee.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class NotificationCreatedAtTests
{
    private readonly DatabaseFixture _fixture;

    public NotificationCreatedAtTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private ApplicationDbContext CreateAuditedContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .AddInterceptors(
                new AuditableEntityInterceptor(
                    new TestCurrentUser(),
                    DatabaseFixture.Clock))
            .Options;

        return new ApplicationDbContext(options);
    }

    [SkippableFact]
    public async Task The_persisted_created_at_is_a_real_timestamp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using var context = CreateAuditedContext();

        var userId = await context.Users.Where(u => u.IsActive).OrderBy(u => u.Id).Select(u => u.Id).FirstAsync();

        var notification = new Notification(userId, NotificationType.SlaBreached, "probe", "Body.");
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        var stored = await context.Notifications
            .AsNoTracking()
            .Where(n => n.Id == notification.Id)
            .Select(n => new { n.CreatedAt, n.CreatedBy })
            .FirstAsync();

        stored.CreatedAt.Should().NotBe(default, "the interceptor stamps this on save");
    }

    [SkippableFact]
    public async Task The_projection_reports_the_stored_created_at_and_the_enum_name()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        await using var context = CreateAuditedContext();

        var userId = await context.Users.Where(u => u.IsActive).OrderBy(u => u.Id).Select(u => u.Id).FirstAsync();

        var notification = new Notification(userId, NotificationType.SlaBreached, "probe", "Body.");
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        await using var reader = _fixture.CreateContext();
        var reloaded = await reader.Notifications
            .AsNoTracking()
            .FirstAsync(n => n.Id == notification.Id);

        var viaProjection = await reader.Notifications
            .AsNoTracking()
            .Where(n => n.Id == notification.Id)
            .Select(n => new NotificationDto(
                n.Id,
                n.UserId,
                n.Type.ToString(),
                n.Title,
                n.Message,
                n.EntityName,
                n.EntityId,
                n.IsRead,
                n.ReadAt,
                n.CreatedAt))
            .FirstAsync();

        viaProjection.CreatedAt.Should().NotBe(default, "the projection reads the stored column");
        viaProjection.CreatedAt.Should().Be(reloaded.CreatedAt, "both read the same persisted value");
        viaProjection.Type.Should().Be("SlaBreached", "the enum reaches the client as its name");
    }
}
