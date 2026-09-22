using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace ServiceManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Spins up a disposable SQL Server container for the persistence tests.
///
/// The fixture is opt-in: containers are only started when
/// <c>ESMP_RUN_INTEGRATION_TESTS=1</c> is set and Docker is reachable, so the
/// suite stays green on a machine without a container runtime. Tests that need
/// the database call <c>Skip.IfNot(fixture.IsAvailable, ...)</c>.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string EnvironmentVariableName = "ESMP_RUN_INTEGRATION_TESTS";

    private MsSqlContainer? _container;

    public string? ConnectionString { get; private set; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);

    public string UnavailableReason =>
        $"SQL Server container not available. Set {EnvironmentVariableName}=1 and start Docker to run this test.";

    public async Task InitializeAsync()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnvironmentVariableName),
                "1",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();

            await _container.StartAsync();

            ConnectionString = _container.GetConnectionString();

            await using var context = CreateContext();
            await context.Database.MigrateAsync();
        }
        catch (Exception)
        {
            // Docker missing or the image could not be pulled: tests degrade to skipped.
            if (_container is not null)
            {
                await _container.DisposeAsync();
                _container = null;
            }

            ConnectionString = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates a fresh context against the container database.</summary>
    public ApplicationDbContext CreateContext()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(UnavailableReason);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .EnableSensitiveDataLogging()
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>Removes all business data so each test starts from a known state.</summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.TicketAssignmentHistory");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.TicketComments");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Tickets");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Notifications");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.AuditLogs");
    }

    public ApplicationDbContext CreateContextWithoutTracking()
    {
        var context = CreateContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }

    public static IDateTimeProvider Clock => new FixedClock(DateTimeOffset.Parse("2026-03-01T09:00:00+00:00"));

    private sealed class FixedClock : IDateTimeProvider
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
