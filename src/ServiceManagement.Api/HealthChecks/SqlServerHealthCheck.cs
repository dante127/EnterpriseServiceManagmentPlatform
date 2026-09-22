using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceManagement.Infrastructure.Persistence;

namespace ServiceManagement.Api.HealthChecks;

/// <summary>Readiness probe for SQL Server.</summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;

    public SqlServerHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reachable = await _dbContext.Database.CanConnectAsync(cancellationToken);

            return reachable
                ? HealthCheckResult.Healthy("SQL Server is reachable.")
                : HealthCheckResult.Unhealthy("SQL Server responded but the connection could not be established.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("SQL Server is unreachable.", exception);
        }
    }
}
