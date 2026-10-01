using Serilog;
using Serilog.Events;
using ServiceManagement.Api;
using ServiceManagement.Api.Extensions;
using ServiceManagement.Infrastructure;

// The container image ships no curl or wget, so the health probe is served by
// the application itself: the orchestrator runs the same binary with
// `--health-check` and the process exits non-zero when the API is not serving.
if (args.Contains("--health-check"))
{
    return await HealthCheckCommand.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

// Structured logging: console for containers, rolling file for local runs.
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ServiceManagement.Api")
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
    .WriteTo.Console(
        outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiLayer(builder.Configuration, builder.Environment);

// No endpoint accepts uploads, so a request body above ~1 MB is either a bug or
// an attack; Kestrel's ~28 MB default is an unbounded DoS surface for a JSON API.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024;
});

var app = builder.Build();

await app.InitialiseDatabaseAsync();

app.UseApiLayer();

app.Logger.LogInformation(
    "{ApplicationName} started in {Environment}. Swagger is available in development.",
    "Service Management API",
    app.Environment.EnvironmentName);

await app.RunAsync();

return 0;

/// <summary>Exposed so integration/API tests can boot the real host.</summary>
public partial class Program
{
}
