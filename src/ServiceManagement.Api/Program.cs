using Serilog;
using Serilog.Events;
using ServiceManagement.Api.Extensions;
using ServiceManagement.Infrastructure;

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
builder.Services.AddApiLayer(builder.Configuration);

var app = builder.Build();

await app.InitialiseDatabaseAsync();

app.UseApiLayer();

app.Logger.LogInformation(
    "{ApplicationName} started in {Environment}. Swagger is available in development.",
    "Service Management API",
    app.Environment.EnvironmentName);

await app.RunAsync();

/// <summary>Exposed so integration/API tests can boot the real host.</summary>
public partial class Program
{
}
