using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Infrastructure.Persistence;

/// <summary>
/// Hands out ticket numbers from the database sequence created by the initial
/// migration. Using a sequence keeps numbering atomic under concurrency without
/// a locking counter table.
/// </summary>
public sealed class TicketNumberGenerator : ITicketNumberGenerator
{
    private readonly IApplicationDbContext _dbContext;

    public TicketNumberGenerator(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        var next = await _dbContext.Database
            .SqlQueryRaw<long>("SELECT NEXT VALUE FOR dbo.TicketNumbers AS [Value]")
            .SingleAsync(cancellationToken);

        return $"TKT-{next:D6}";
    }
}
