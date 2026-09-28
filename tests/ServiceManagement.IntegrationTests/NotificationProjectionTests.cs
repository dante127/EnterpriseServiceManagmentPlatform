using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Notifications;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The notification list used to materialise the entity and adapt it in memory
/// with Mapster. Every other query in the codebase uses an explicit EF projection,
/// which keeps the SQL visible and stops a client from receiving only the columns
/// a DTO happens to name.
/// <para>
/// The subtlety is <see cref="NotificationDto.Type"/>: the entity stores an enum
/// and the DTO exposes a string. A hand-written projection has to convert it in
/// the database, and a failed translation would surface at runtime rather than
/// compile time. These tests pin the conversion and the column selection.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class NotificationProjectionTests
{
    private readonly DatabaseFixture _fixture;

    public NotificationProjectionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task The_projection_renders_the_enum_type_as_its_name()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await FirstActiveUserIdAsync();
        await SeedAsync(userId, NotificationType.SlaBreached, "breach");
        await SeedAsync(userId, NotificationType.TicketAssigned, "assigned");

        var handler = new GetNotificationsQueryHandler(_fixture.CreateContext(), new StubUser(userId));
        var result = await handler.Handle(
            new GetNotificationsQuery(IsRead: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        result.Items.Should().HaveCount(2);

        result.Items.Select(item => item.Type).Should().BeEquivalentTo(
            ["SlaBreached", "TicketAssigned"],
            "the client receives the enum name, not its numeric value");
    }

    [SkippableFact]
    public async Task The_projection_returns_every_documented_field()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await FirstActiveUserIdAsync();
        await SeedAsync(userId, NotificationType.SlaNearBreach, "at risk", "Ticket", "TKT-42");

        var handler = new GetNotificationsQueryHandler(_fixture.CreateContext(), new StubUser(userId));
        var result = await handler.Handle(
            new GetNotificationsQuery(IsRead: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Subject;

        item.Title.Should().Be("at risk");
        item.Message.Should().NotBeNullOrWhiteSpace();
        item.EntityName.Should().Be("Ticket");
        item.EntityId.Should().Be("TKT-42");
        item.IsRead.Should().BeFalse();
        item.ReadAt.Should().BeNull();
        item.UserId.Should().Be(userId);
        // CreatedAt is written by the audit interceptor, which the bare test context
        // omits. NotificationCreatedAtTests covers it with a production-like context.
        item.Id.Should().NotBe(Guid.Empty);
    }

    [SkippableFact]
    public async Task A_notification_with_no_entity_reference_projects_nulls_not_empty_strings()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await FirstActiveUserIdAsync();
        await SeedAsync(userId, NotificationType.SlaBreached, "no reference");

        var handler = new GetNotificationsQueryHandler(_fixture.CreateContext(), new StubUser(userId));
        var result = await handler.Handle(
            new GetNotificationsQuery(IsRead: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Subject;

        item.EntityName.Should().BeNull();
        item.EntityId.Should().BeNull();
    }

    [SkippableFact]
    public async Task The_projection_is_executed_by_the_database_not_after_materialisation()
    {
        // The whole point of dropping Mapster: the query selects the DTO's columns
        // and the database does the work. If someone reintroduces ToListAsync plus
        // an in-memory map, the command count and the selected columns both change.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await FirstActiveUserIdAsync();
        await SeedAsync(userId, NotificationType.SlaBreached, "projection");

        var interceptor = new CommandCapturingInterceptor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new ApplicationDbContext(options);
        var handler = new GetNotificationsQueryHandler(context, new StubUser(userId));

        await handler.Handle(
            new GetNotificationsQuery(IsRead: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        var listSql = interceptor.Commands
            .Where(sql => sql.Contains("[Notifications]", StringComparison.OrdinalIgnoreCase)
                        && sql.Contains("FROM", StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();

        listSql.Should().NotBeNull("the notification list must be one query");

        // A projection names the DTO's columns; a materialisation selects the whole
        // entity, including CorrelationId, which no client receives.
        listSql!.ToLowerInvariant().Should().Contain("[type]");
        listSql.ToLowerInvariant().Should().NotContain("[correlationid]");
    }

    private async Task SeedAsync(
        Guid userId,
        NotificationType type,
        string title,
        string? entityName = null,
        string? entityId = null)
    {
        await using var context = _fixture.CreateContext();

        context.Notifications.Add(new Notification(
            userId,
            type,
            title,
            $"Body for {title}.",
            entityName,
            entityId,
            "test-correlation"));

        await context.SaveChangesAsync();
    }

    private async Task<Guid> FirstActiveUserIdAsync()
    {
        await using var context = _fixture.CreateContext();
        return await context.Users.Where(user => user.IsActive).OrderBy(user => user.Id).Select(user => user.Id).FirstAsync();
    }

    private sealed class CommandCapturingInterceptor
        : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>
            ReaderExecuting(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>>
            ReaderExecutingAsync(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubUser(Guid userId) : ServiceManagement.Application.Common.Interfaces.ICurrentUserService
    {
        public Guid? UserId => userId;

        public string? UserName => "stub";

        public string? Email => "stub@test.local";

        public bool IsAuthenticated => true;

        public System.Collections.Generic.IReadOnlyCollection<string> Roles => [];

        public System.Collections.Generic.IReadOnlyCollection<string> Permissions => [];

        public string? IpAddress => "127.0.0.1";

        public string? CorrelationId => "test-correlation";

        public bool HasPermission(string permission) => true;

        public Guid RequireUserId() => userId;
    }
}
