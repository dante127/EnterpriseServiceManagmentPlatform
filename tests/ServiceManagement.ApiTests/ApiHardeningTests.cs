using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceManagement.ApiTests.Infrastructure;

namespace ServiceManagement.ApiTests;

/// <summary>
/// Production hardening visible from the outside: the static security headers on
/// every response, and the composition root's refusal to boot a production
/// deployment on the development signing key published in this repository.
/// </summary>
public class ApiHardeningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiHardeningTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Every_response_carries_the_static_security_headers()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var headerNames = response.Headers.Select(header => header.Key).ToList();

        headerNames.Should().Contain("X-Content-Type-Options");
        headerNames.Should().Contain("X-Frame-Options");
        headerNames.Should().Contain("Referrer-Policy");
    }

    [Fact]
    public void A_production_stack_refuses_to_boot_on_the_development_signing_key()
    {
        // The key shipped in appsettings.Development.json and .env.example is
        // public in the repository; minting admin tokens from it must fail at
        // startup, not after the first forged request.
        var factory = new ApiFactory
        {
            UseProductionEnvironment = true,
            JwtSigningKey = "dev-only-signing-key-please-replace-in-production-1234567890"
        };

        var act = () => factory.CreateClient();

        var exception = act.Should().Throw<Exception>().Which;
        var messages = new List<string>();
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        messages.Should().Contain(
            message => message.Contains("development key", StringComparison.OrdinalIgnoreCase),
            "the refusal must name the actual problem, not just 'options invalid'");
    }

    [Fact]
    public async Task A_production_stack_boots_on_a_real_signing_key()
    {
        using var factory = new ApiFactory
        {
            UseProductionEnvironment = true,
            JwtSigningKey = "production-test-signing-key-that-is-long-enough-1234567890"
        };

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
