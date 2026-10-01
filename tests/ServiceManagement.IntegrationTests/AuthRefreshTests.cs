using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Auth;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Infrastructure.Auditing;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Refresh-token rotation: the presented token is revoked exactly once and
/// replaced, so a stolen token can be used at most once. The revoke is a
/// compare-and-swap update - a load-check-save sequence here would let two
/// concurrent refreshes with the same token both mint valid replacements.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class AuthRefreshTests
{
    private readonly DatabaseFixture _fixture;

    public AuthRefreshTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_refresh_token_rotates_and_the_old_token_is_linked_to_its_successor()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (login, refresh) = CreateHandlers(out var jwt);
        var first = await login.Handle(
            new LoginCommand(DatabaseSeeder.AdminEmail, DatabaseSeeder.AdminPassword),
            CancellationToken.None);

        var rotated = await refresh.Handle(new RefreshTokenCommand(first.RefreshToken), CancellationToken.None);

        rotated.RefreshToken.Should().NotBe(first.RefreshToken, "rotation must issue a new token");
        rotated.User.Id.Should().Be(first.User.Id);

        await using var context = _fixture.CreateContext();
        var oldToken = await context.RefreshTokens.AsNoTracking()
            .SingleAsync(token => token.TokenHash == jwt.HashRefreshToken(first.RefreshToken));
        var newToken = await context.RefreshTokens.AsNoTracking()
            .SingleAsync(token => token.TokenHash == jwt.HashRefreshToken(rotated.RefreshToken));

        oldToken.RevokedAt.Should().NotBeNull();
        oldToken.ReasonRevoked.Should().Be("Rotated");
        oldToken.ReplacedByTokenHash.Should().Be(newToken.TokenHash, "rotation must record the successor");
        newToken.RevokedAt.Should().BeNull();

        (await context.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.Action == AuditActions.TokenRefreshed && entry.UserId == first.User.Id))
            .Should().Be(1, "the refresh must be auditable");
    }

    [SkippableFact]
    public async Task Replaying_a_rotated_token_revokes_every_active_token_of_the_user()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (login, refresh) = CreateHandlers(out var jwt);

        // Two independent sessions for the same user, then rotate the first.
        var sessionOne = await login.Handle(
            new LoginCommand(DatabaseSeeder.AdminEmail, DatabaseSeeder.AdminPassword),
            CancellationToken.None);
        var sessionTwo = await login.Handle(
            new LoginCommand(DatabaseSeeder.AdminEmail, DatabaseSeeder.AdminPassword),
            CancellationToken.None);

        await refresh.Handle(new RefreshTokenCommand(sessionOne.RefreshToken), CancellationToken.None);

        // The rotated token coming back is the compromise signal.
        var act = async () => await refresh.Handle(
            new RefreshTokenCommand(sessionOne.RefreshToken),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();

        await using var context = _fixture.CreateContext();
        var userId = sessionOne.User.Id;

        (await context.RefreshTokens.AsNoTracking().CountAsync(token => token.UserId == userId && token.RevokedAt == null))
            .Should().Be(0, "reuse detection must sign every session out, not just the replayed one");

        (await context.RefreshTokens.AsNoTracking().CountAsync(token =>
                token.TokenHash == jwt.HashRefreshToken(sessionTwo.RefreshToken)
                && token.ReasonRevoked == "ReuseDetected"))
            .Should().Be(1);
    }

    [SkippableFact]
    public async Task An_unrecognised_refresh_token_is_rejected()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var (_, refresh) = CreateHandlers(out _);

        var act = async () => await refresh.Handle(
            new RefreshTokenCommand("no-such-token-anywhere-in-the-database"),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    private (LoginCommandHandler Login, RefreshTokenCommandHandler Refresh) CreateHandlers(
        out JwtTokenService jwt)
    {
        var caller = new TestCurrentUser();
        var context = _fixture.CreateContext();
        jwt = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "ServiceManagement.Tests",
            Audience = "ServiceManagement.Tests.Clients",
            SigningKey = "integration-test-signing-key-that-is-long-enough-1234567890"
        }));

        var login = new LoginCommandHandler(
            context,
            new IdentityPasswordHasher(),
            jwt,
            new AuditService(context, caller, DatabaseFixture.Clock),
            caller,
            DatabaseFixture.Clock,
            Options.Create(new AuthOptions()));

        var refresh = new RefreshTokenCommandHandler(
            context,
            jwt,
            new AuditService(context, caller, DatabaseFixture.Clock),
            caller,
            DatabaseFixture.Clock,
            Options.Create(new AuthOptions()));

        return (login, refresh);
    }
}
