using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using ServiceManagement.ApiTests.Infrastructure;

namespace ServiceManagement.ApiTests;

/// <summary>
/// Smoke tests that run without any infrastructure: the host must boot and its
/// HTTP surface must behave even when SQL Server and Redis are absent.
/// </summary>
public class ApiSmokeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiSmokeTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_liveness_probe_answers_healthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("status");
    }

    [Fact]
    public async Task Swagger_is_not_exposed_outside_development()
    {
        // The API surface must not be published in a deployed environment: it hands
        // an attacker the full route list and the endpoint documentation.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Swagger_exposes_the_versioned_document_in_development()
    {
        using var factory = new ApiFactory { UseDevelopmentEnvironment = true };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.IsSuccessStatusCode.Should().BeTrue();

        var json = await response.Content.ReadAsStringAsync();
        var document = JsonNode.Parse(json);

        document.Should().NotBeNull();
        document!["openapi"]!.ToString().Should().StartWith("3.");
        document["paths"].Should().NotBeNull();
    }

    [Theory]
    [InlineData("/api/v1/tickets")]
    [InlineData("/api/v1/reports/dashboard")]
    [InlineData("/api/v1/notifications")]
    [InlineData("/api/v1/audit")]
    public async Task Protected_endpoints_require_authentication(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":false").And.Contain("message");
    }

    [Fact]
    public async Task Unknown_routes_still_return_json_from_the_exception_handler()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("message");
    }

    [Fact]
    public async Task The_correlation_id_is_echoed_back()
    {
        using var client = _factory.CreateClientWithCorrelationHeader();

        var response = await client.GetAsync("/health/live");

        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
    }
}
