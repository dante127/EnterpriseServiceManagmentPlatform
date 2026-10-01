using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using Testcontainers.MsSql;

namespace ServiceManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Provides a real SQL Server for the persistence tests. A database is the only
/// way to prove the parts EF cannot fake: rowversion concurrency, filtered
/// unique indexes and the ticket-number sequence.
/// <para>
/// Provisioning order:
/// </para>
/// <list type="number">
/// <item>an explicit connection string (<c>ESMP_TEST_SQL_CONNECTION</c>) - used verbatim;</item>
/// <item>SQL Server LocalDB - no container runtime needed, preferred on developer machines;</item>
/// <item>Testcontainers - requires a reachable Docker daemon.</item>
/// </list>
/// <para>
/// Every step degrades to a skip rather than a failure, so a machine with no
/// database at all still produces a green (and honest) run.
/// </para>
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>Overrides the connection string entirely, e.g. in CI.</summary>
    public const string ConnectionStringVariableName = "ESMP_TEST_SQL_CONNECTION";

    /// <summary>Set to 0 to skip LocalDB even when it is installed.</summary>
    public const string DisableLocalDbVariableName = "ESMP_TEST_SKIP_LOCALDB";

    private const string LocalDbInstanceName = "MSSQLLocalDB";

    private MsSqlContainer? _container;

    public string? ConnectionString { get; private set; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);

    public string UnavailableReason =>
        $"No SQL Server available. Set {ConnectionStringVariableName}, or install SQL Server "
        + $"LocalDB (instance '{LocalDbInstanceName}'), or start Docker for Testcontainers.";

    public async Task InitializeAsync()
    {
        var requested = Environment.GetEnvironmentVariable(ConnectionStringVariableName);

        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!TryUseConnectionString(requested))
            {
                // An explicitly requested endpoint that cannot be reached is a CI
                // configuration failure, not an optional feature. Falling through
                // to LocalDB would silently skip every test and turn a broken
                // suite green - the exact failure mode this fixture once had.
                throw new InvalidOperationException(
                    $"{ConnectionStringVariableName} is set, but its SQL Server is unreachable "
                    + $"or could not be migrated: {Redact(requested)}");
            }

            Report("explicit connection string");
            return;
        }

        if (Environment.GetEnvironmentVariable(DisableLocalDbVariableName) != "1" &&
            TryUseLocalDb())
        {
            Report($"LocalDB instance '{LocalDbInstanceName}'");
            return;
        }

        await TryUseTestcontainersAsync();

        if (IsAvailable)
        {
            Report("Testcontainers");
        }
    }

    /// <summary>
    /// LocalDB stops itself when idle, so a connection that worked a moment ago
    /// can be refused later. Retrying once after a brief pause makes the fixture
    /// survive that without the developer restarting anything.
    /// </summary>
    private bool TryUseLocalDb()
    {
        var connectionString =
            $"Server=(localdb)\\{LocalDbInstanceName};Database=ServiceManagementTests;"
            + "Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10";

        if (TryConnect(connectionString))
        {
            return true;
        }

        if (TryStartLocalDbInstance() && TryConnect(connectionString))
        {
            return true;
        }

        return false;
    }

    private static bool TryStartLocalDbInstance()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sqllocaldb",
                ArgumentList = { "start", LocalDbInstanceName },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(30_000);
            return process?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Report(string source)
        => Console.Error.WriteLine($"[ESMP tests] SQL Server ready via {source}: {Redact(ConnectionString!)}");

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates a fresh context against the provisioned database.</summary>
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

    /// <summary>
    /// Removes all data - business, identity and catalog - so each test starts
    /// from the fully seeded state.
    /// <para>
    /// Identity state (users, tokens, roles, org structure) must be cleared along
    /// with business data: tests mutate passwords, mint refresh tokens and revoke
    /// roles, and the database persists across runs, so a test that leaks auth
    /// state breaks whichever test runs next. The seeder rebuilds everything from
    /// empty, which is what makes the reset deterministic rather than "known, for
    /// small values of known".
    /// </para>
    /// </summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();

        // Order matters: each delete names rows the next table references.
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.TicketAssignmentHistory");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.TicketComments");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Tickets");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Notifications");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.AuditLogs");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.RefreshTokens");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.UserRoles");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.RolePermissions");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.TeamMembers");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Teams");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Users");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Roles");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Permissions");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Departments");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Organizations");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.Services");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.SlaPolicies");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.ServicePriorities");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM dbo.ServiceCategories");

        await SeedAsync();
    }

    /// <summary>
    /// Clears the service catalog as well as everything <see cref="ResetAsync"/>
    /// clears, then re-seeds it. Tests that add catalog rows call this to make
    /// their intent explicit; the reset itself now covers the catalog, so the
    /// leaked-row hazard it used to guard against (a second active policy sharing
    /// the fixture clock's CreatedAt) no longer exists.
    /// </summary>
    public async Task ResetCatalogAsync()
    {
        await ResetAsync();
    }

    /// <summary>
    /// Seeds the catalog and the demo users if they are not already present.
    /// <para>
    /// This is deliberately idempotent and cheap. Helpers such as
    /// <c>TestTicket.CreateAsync</c> look up a service, a priority and the demo
    /// users by name; they rely on the catalog existing, but do not seed it
    /// themselves. Making the fixture guarantee it removes that hidden dependency
    /// on test execution order, which is what produced a suite that only failed
    /// intermittently.
    /// </para>
    /// </summary>
    public async Task SeedAsync()
    {
        await using var context = CreateContext();

        // The guard is on the *catalog*, not on users. DatabaseSeeder returns early
        // whenever any user exists, so checking users would leave the catalog
        // missing after a catalog-only reset - the exact case SeedAsync is here to
        // repair.
        if (await context.Services.AnyAsync()
            && await context.ServicePriorities.AnyAsync()
            && await context.ServiceCategories.AnyAsync()
            && await context.SlaPolicies.AnyAsync())
        {
            return;
        }

        // DatabaseSeeder itself is a no-op once a user exists, so a partially
        // cleared catalog is rebuilt directly rather than through it.
        if (await context.Users.AnyAsync())
        {
            await RebuildCatalogAsync(context);
            return;
        }

        await ServiceManagement.Infrastructure.Persistence.Seed.DatabaseSeeder.SeedAsync(
            context,
            new IdentityPasswordHasher(),
            Clock);
    }

    /// <summary>
    /// Re-creates just the catalog. Needed when the users survived a reset but the
    /// catalog did not: DatabaseSeeder treats seeding as a one-time bootstrap and
    /// refuses to touch existing data.
    /// </summary>
    private static async Task RebuildCatalogAsync(ApplicationDbContext context)
    {
        var categories = new[]
        {
            new ServiceCategory("Hardware", "HW"),
            new ServiceCategory("Software", "SW"),
            new ServiceCategory("Network", "NET"),
            new ServiceCategory("Access", "ACC")
        };

        var priorities = new[]
        {
            new ServicePriority("Critical", "P1", 1, "#DC2626"),
            new ServicePriority("High", "P2", 2, "#EA580C"),
            new ServicePriority("Medium", "P3", 3, "#CA8A04"),
            new ServicePriority("Low", "P4", 4, "#15803D")
        };

        context.ServiceCategories.AddRange(categories);
        context.ServicePriorities.AddRange(priorities);
        await context.SaveChangesAsync();

        var policies = new[]
        {
            new SlaPolicy("Critical SLA", priorities[0].Id, firstResponseMinutes: 30, resolutionMinutes: 240, atRiskThresholdPercent: 20),
            new SlaPolicy("High SLA", priorities[1].Id, firstResponseMinutes: 60, resolutionMinutes: 480, atRiskThresholdPercent: 20),
            new SlaPolicy("Medium SLA", priorities[2].Id, firstResponseMinutes: 240, resolutionMinutes: 1440, atRiskThresholdPercent: 25),
            new SlaPolicy("Low SLA", priorities[3].Id, firstResponseMinutes: 480, resolutionMinutes: 2880, atRiskThresholdPercent: 25)
        };

        context.SlaPolicies.AddRange(policies);
        await context.SaveChangesAsync();

        context.Services.AddRange(
            new Service(categories[0].Id, "IT Support", "IT-SUP", "Hardware incidents and requests.", priorities[0].Id, policies[0].Id),
            new Service(categories[1].Id, "Email Support", "EMAIL-SUP", "Mailbox and collaboration issues.", priorities[2].Id, policies[2].Id));

        await context.SaveChangesAsync();
    }

    public ApplicationDbContext CreateContextWithoutTracking()
    {
        var context = CreateContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }

    public static IDateTimeProvider Clock => new FixedClock(DateTimeOffset.Parse("2026-03-01T09:00:00+00:00"));

    private bool TryUseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (TryConnect(connectionString))
        {
            return true;
        }

        return false;
    }

    private async Task TryUseTestcontainersAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
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

    /// <summary>
    /// Verifies the database is genuinely reachable and migrated, so a test never
    /// skips because of a stale or half-applied schema.
    /// </summary>
    private bool TryConnect(string connectionString)
    {
        try
        {
            ConnectionString = connectionString;

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(connectionString)
                .EnableSensitiveDataLogging()
                .Options;

            using var context = new ApplicationDbContext(options);
            context.Database.Migrate();

            return true;
        }
        catch (Exception exception)
        {
            // Keep the reason visible: a silent skip is how the concurrency tests
            // went unnoticed in the first place.
            Console.Error.WriteLine($"[ESMP tests] '{Redact(connectionString)}' unavailable: {exception.Message}");
            ConnectionString = null;
            return false;
        }
    }

    private static string Redact(string connectionString)
    {
        var builder = new System.Text.StringBuilder(connectionString);

        foreach (var marker in new[] { "Password=", "Pwd=" })
        {
            var index = builder.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            while (index >= 0)
            {
                var end = builder.ToString().IndexOf(';', index);
                end = end < 0 ? builder.Length : end;

                builder.Remove(index + marker.Length, end - index - marker.Length);
                builder.Insert(index + marker.Length, "***");

                var next = builder.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (next < 0)
                {
                    break;
                }

                index = next;
            }
        }

        return builder.ToString();
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
