using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceManagement.Api;

namespace ServiceManagement.ApiTests.Infrastructure;

/// <summary>
/// Boots the real API host with WebApplicationFactory. Migrations, background
/// jobs and Redis are disabled so smoke tests run without infrastructure.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Placeholder used when a test does not supply a real database. No test that
    /// needs a database runs without a container, so this is never connected to.
    /// </summary>
    private const string FallbackConnectionString =
        "Server=localhost,1433;Database=ServiceManagementApiTests;User Id=sa;"
        + "Pwd=IntegrationOnly1!;TrustServerCertificate=True;Connect Timeout=2";

    public string? DatabaseConnectionString { get; set; }

    public string JwtSigningKey { get; set; } =
        "api-test-signing-key-that-is-long-enough-1234567890";

    /// <summary>
    /// Switches the host to the Development environment. Only the tests that
    /// assert environment-gated behaviour (such as Swagger) need this.
    /// </summary>
    public bool UseDevelopmentEnvironment { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(UseDevelopmentEnvironment ? "Development" : "Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = DatabaseConnectionString ?? FallbackConnectionString,

                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["BackgroundJobs:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Notifications:EmailEnabled"] = "false",

                ["Jwt:Issuer"] = "ServiceManagement.Api.Tests",
                ["Jwt:Audience"] = "ServiceManagement.Tests",
                ["Jwt:SigningKey"] = JwtSigningKey,
                ["Jwt:AccessTokenMinutes"] = "30",

                ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
            });
        });
    }

    /// <summary>Client that already carries a correlation id header.</summary>
    public HttpClient CreateClientWithCorrelationHeader()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.CreateVersion7().ToString());
        return client;
    }
}
