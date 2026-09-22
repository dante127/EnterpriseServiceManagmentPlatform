using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using ServiceManagement.Api.Extensions;
using ServiceManagement.Api.Middleware;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;

namespace ServiceManagement.Api.Extensions;

/// <summary>Pipeline construction and startup data initialisation.</summary>
public static class WebApplicationExtensions
{
    public static WebApplication UseApiLayer(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Correlation id first so every later component (and the audit trail) can use it.
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseExceptionHandler();

        app.UseSerilogRequestLogging();

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Service Management API v1");
            options.DocumentTitle = "Service Management Platform API";
        });

        app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // Unmatched routes answer with the same envelope as everything else.
        app.MapFallback(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";

            return context.Response.WriteAsJsonAsync(new
            {
                success = false,
                data = (object?)null,
                message = $"No endpoint matches {context.Request.Method} {context.Request.Path}.",
                errors = Array.Empty<object>()
            });
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthReportAsync
        });

        // Liveness: the process is up. No dependency is checked.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthReportAsync
        });

        // Readiness: SQL Server and Redis must both be reachable.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteHealthReportAsync
        });

        return app;
    }

    /// <summary>
    /// Applies pending migrations and, in development, seeds reference data.
    /// <c>EnsureCreated</c> is deliberately not used: the schema is versioned.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var databaseOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (!databaseOptions.ApplyMigrationsOnStartup)
        {
            return;
        }

        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var logger = services.GetRequiredService<ILogger<WebApplication>>();

        try
        {
            await dbContext.Database.MigrateAsync();

            if (app.Environment.IsDevelopment())
            {
                await DatabaseSeeder.SeedAsync(
                    dbContext,
                    services.GetRequiredService<IPasswordHasher>(),
                    services.GetRequiredService<IDateTimeProvider>());

                logger.LogInformation("Development seed data verified.");
            }
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Database initialisation failed. Verify the connection string and that SQL Server is reachable.");
            throw;
        }
    }

    private static Task WriteHealthReportAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
                description = entry.Value.Description
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
