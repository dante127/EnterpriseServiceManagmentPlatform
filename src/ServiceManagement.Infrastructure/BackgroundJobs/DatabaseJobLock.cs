using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Infrastructure.BackgroundJobs;

/// <summary>
/// Cross-instance mutual exclusion for background jobs, built on SQL Server
/// application locks (<c>sp_getapplock</c>). A job either owns the lock and runs,
/// or skips the tick entirely - so running several API replicas can never
/// execute the same maintenance cycle twice.
/// </summary>
internal static class DatabaseJobLock
{
    private const int LockTimeoutMilliseconds = 0;

    public static async Task<bool> TryAcquireAsync(
        ApplicationDbContext context,
        string resource,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var returnCode = await ExecuteLockCommandAsync(
            connection,
            "sp_getapplock",
            resource,
            cancellationToken);

        if (returnCode < 0 && openedHere)
        {
            await connection.CloseAsync();
        }

        return returnCode >= 0;
    }

    public static async Task ReleaseAsync(
        ApplicationDbContext context,
        string resource,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        await ExecuteLockCommandAsync(connection, "sp_releaseapplock", resource, cancellationToken);
    }

    private static async Task<int> ExecuteLockCommandAsync(
        DbConnection connection,
        string procedure,
        string resource,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = procedure;
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add(CreateParameter(command, "@Resource", resource));
        command.Parameters.Add(CreateParameter(command, "@LockMode", "Exclusive"));
        command.Parameters.Add(CreateParameter(command, "@LockOwner", "Session"));
        command.Parameters.Add(CreateParameter(command, "@LockTimeout", LockTimeoutMilliseconds));

        var returnValue = command.CreateParameter();
        returnValue.ParameterName = "@result";
        returnValue.DbType = DbType.Int32;
        returnValue.Direction = ParameterDirection.ReturnValue;
        command.Parameters.Add(returnValue);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return returnValue.Value is null or DBNull
            ? int.MinValue
            : Convert.ToInt32(returnValue.Value);
    }

    private static DbParameter CreateParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }
}
