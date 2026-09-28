using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Infrastructure.Persistence;

/// <summary>
/// Hands out ticket numbers from the database sequence created by the initial
/// migration. Using a sequence keeps numbering atomic under concurrency without
/// a locking counter table.
/// </summary>
public sealed class TicketNumberGenerator : ITicketNumberGenerator
{
    /// <summary>
    /// The sequence is read through raw ADO rather than <c>SqlQueryRaw</c>: EF
    /// wraps composed SQL in a derived table, and SQL Server rejects
    /// <c>NEXT VALUE FOR</c> inside one.
    /// </summary>
    private const string NextValueSql = "SELECT NEXT VALUE FOR dbo.TicketNumbers;";

    private readonly IApplicationDbContext _dbContext;

    public TicketNumberGenerator(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = NextValueSql;
        command.CommandType = CommandType.Text;

        // A raw command does not automatically join a transaction that is already
        // open on the connection. Once a handler runs inside a unit of work, SQL
        // Server rejects the command with "BeginExecuteReader requires the command
        // to have a transaction", so the ambient transaction is attached here.
        // Sequence reads are not transactional anyway: rolling back consumes the
        // value and leaves a gap, which is the intended, documented behaviour.
        var transaction = _dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);

        var next = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);

        return $"TKT-{next:D6}";
    }
}
