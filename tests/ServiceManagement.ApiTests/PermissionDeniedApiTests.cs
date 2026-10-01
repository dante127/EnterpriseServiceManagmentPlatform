using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceManagement.ApiTests.Infrastructure;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Infrastructure.Security;

namespace ServiceManagement.ApiTests;

/// <summary>
/// Authorization must deny an authenticated caller who lacks the permission.
/// Until now the only permission tests were 401s without a token, so a wiring
/// bug in the policy provider would have opened every endpoint silently.
/// <para>
/// The token is minted locally with the test host's own signing key, so the
/// test needs no database: authorization fails before any handler runs.
/// </para>
/// </summary>
public class PermissionDeniedApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PermissionDeniedApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task An_authenticated_caller_without_the_permission_is_forbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", MintTokenWithNoPermissions());

        var response = await client.GetAsync("/api/v1/audit");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private string MintTokenWithNoPermissions()
    {
        var jwt = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "ServiceManagement.Api.Tests",
            Audience = "ServiceManagement.Tests",
            SigningKey = _factory.JwtSigningKey
        }));

        // A user whose session carries no roles and no permission claims: the
        // bearer token authenticates, nothing authorizes.
        var user = new User("noaccess@test.local", "noaccess", "No", "Access", "unused");

        return jwt.CreateAccessToken(user, roles: [], permissions: []).Token;
    }
}
