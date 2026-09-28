using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Application.Common.Persistence;

/// <summary>
/// Runs a unit of work inside an explicit database transaction.
/// <para>
/// A business change, the notifications it raises and its audit record must
/// commit together. Once the audit and notification services stage onto the
/// caller's context, a single <c>SaveChangesAsync</c> would already be atomic,
/// but a handler that saves, then stages, then saves again has crossed a
/// transaction boundary. Wrapping the whole handler keeps that guarantee even if
/// the sequence of calls changes later.
/// </para>
/// <para>
/// The work runs through <see cref="IExecutionStrategy"/> rather than opening a
/// transaction directly. The API configures <c>EnableRetryOnFailure</c>, and that
/// strategy refuses user-initiated transactions: it can only retry correctly if
/// the whole transaction is replayed as one unit. Opening a transaction without
/// the strategy throws at runtime, and dropping the strategy would silently
/// defeat the retry behaviour configured at startup.
/// </para>
/// </summary>
internal static class UnitOfWork
{
    public static Task<T> ExecuteAsync<T>(
        this IApplicationDbContext dbContext,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(work);

        // An ambient transaction is left alone: whoever opened it owns its
        // lifetime, and nesting one here would break their commit boundary.
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return work(cancellationToken);
        }

        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            state: work,
            operation: (_, state, token) => RunAsync(dbContext, state, token),
            verifySucceeded: null,
            cancellationToken);
    }

    /// <inheritdoc cref="ExecuteAsync{T}"/>
    public static Task ExecuteAsync(
        this IApplicationDbContext dbContext,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(work);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return work(cancellationToken);
        }

        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            state: work,
            operation: (_, state, token) => RunVoidAsync(dbContext, state, token),
            verifySucceeded: null,
            cancellationToken);
    }

    private static async Task<T> RunAsync<T>(
        IApplicationDbContext dbContext,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await work(cancellationToken);

            // The caller saves inside `work`; this commits what it wrote.
            await transaction.CommitAsync(cancellationToken);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<ExecutionResult<bool>> RunVoidAsync(
        IApplicationDbContext dbContext,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        var completed = await RunAsync(
            dbContext,
            async token =>
            {
                await work(token);
                return true;
            },
            cancellationToken);

        return new ExecutionResult<bool>(true, completed);
    }
}
