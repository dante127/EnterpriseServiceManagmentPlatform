using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Infrastructure.BackgroundJobs;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// The background workers exclude each other with a SQL Server application lock.
/// <para>
/// The release path was previously wrong: it passed the acquisition parameters
/// (<c>@LockMode</c>, <c>@LockTimeout</c>) to <c>sp_releaseapplock</c>, which accepts
/// only <c>@LockOwner</c> and <c>@Resource</c>. SQL Server rejected the call, so
/// every cycle reported failure even though its work had committed - and, worse,
/// the error was indistinguishable from a genuine failure in the monitoring
/// logic itself.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public class DatabaseJobLockTests
{
    private readonly DatabaseFixture _fixture;

    public DatabaseJobLockTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_lock_can_be_acquired_and_released_without_error()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();

        var acquired = await DatabaseJobLock.TryAcquireAsync(context, "test-job", CancellationToken.None);

        acquired.Should().BeTrue();

        // Would previously throw SqlException: too many arguments specified.
        var release = async () => await DatabaseJobLock.ReleaseAsync(context, "test-job", CancellationToken.None);

        await release.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task Acquiring_and_repeatedly_releasing_the_same_lock_is_stable()
    {
        // A second acquire must succeed after a release, proving the release really
        // happened rather than being silently swallowed.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await DatabaseJobLock.TryAcquireAsync(context, "test-job-repeat", CancellationToken.None))
                .Should().BeTrue($"attempt {attempt} should acquire the lock");

            await DatabaseJobLock.ReleaseAsync(context, "test-job-repeat", CancellationToken.None);
        }
    }

    [SkippableFact]
    public async Task Releasing_a_lock_that_was_never_acquired_does_not_throw()
    {
        // Defensive: the session may already be gone by the time a finally block
        // runs, and a background cycle must not fail over a cleanup detail.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);

        await using var context = _fixture.CreateContext();
        await context.Database.OpenConnectionAsync();

        var release = async () =>
            await DatabaseJobLock.ReleaseAsync(context, "never-acquired", CancellationToken.None);

        await release.Should().NotThrowAsync();
    }
}
