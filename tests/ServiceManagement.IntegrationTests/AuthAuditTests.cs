using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Auth;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Infrastructure.Auditing;
using ServiceManagement.Infrastructure.Persistence;
using ServiceManagement.Infrastructure.Persistence.Seed;
using ServiceManagement.Infrastructure.Security;
using ServiceManagement.IntegrationTests.Infrastructure;
using Xunit;

namespace ServiceManagement.IntegrationTests;

/// <summary>
/// Auth handlers must leave a durable audit record. The audit service only stages
/// rows - they commit with the handler's save - so a handler that stages its audit
/// entry *after* its final SaveChangesAsync writes nothing: the scoped context is
/// disposed with the row still pending. Login, login failure, token refresh and
/// password change all had that bug, which meant the security events an audit
/// trail exists for were exactly the ones missing from it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class AuthAuditTests
{
    private readonly DatabaseFixture _fixture;

    public AuthAuditTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task A_failed_login_leaves_an_audit_record()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await GetUserIdAsync(DatabaseSeeder.AdminEmail);
        var handler = CreateLoginHandler();

        var act = async () => await handler.Handle(
            new LoginCommand(DatabaseSeeder.AdminEmail, "definitely-not-the-password"),
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();

        // The failure path has no other unit-of-work to ride on: if the handler
        // does not flush the staged row itself, this assertion catches it.
        await using var context = _fixture.CreateContext();
        (await context.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.Action == AuditActions.LoginFailed && entry.EntityId == userId.ToString()))
            .Should().Be(1, "a failed sign-in is one of the events an audit trail exists for");
    }

    [SkippableFact]
    public async Task A_successful_login_leaves_an_audit_record_and_a_refresh_token()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await GetUserIdAsync(DatabaseSeeder.AdminEmail);
        var handler = CreateLoginHandler();

        var token = await handler.Handle(
            new LoginCommand(DatabaseSeeder.AdminEmail, DatabaseSeeder.AdminPassword),
            CancellationToken.None);

        token.AccessToken.Should().NotBeNullOrEmpty();
        token.RefreshToken.Should().NotBeNullOrEmpty();

        await using var context = _fixture.CreateContext();
        (await context.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.Action == AuditActions.Login && entry.UserId == userId))
            .Should().Be(1);

        (await context.RefreshTokens.AsNoTracking().CountAsync(t => t.UserId == userId && t.RevokedAt == null))
            .Should().Be(1);
    }

    [SkippableFact]
    public async Task A_password_change_leaves_an_audit_record_and_signs_other_sessions_out()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason);
        await _fixture.ResetAsync();

        var userId = await GetUserIdAsync(DatabaseSeeder.AdminEmail);
        var currentUser = new TestCurrentUser { UserId = userId };

        await using (var context = _fixture.CreateContext())
        {
            var login = CreateLoginHandler(currentUser);
            await login.Handle(
                new LoginCommand(DatabaseSeeder.AdminEmail, DatabaseSeeder.AdminPassword),
                CancellationToken.None);
        }

        await using (var context = _fixture.CreateContext())
        {
            var handler = new ChangePasswordCommandHandler(
                context,
                new IdentityPasswordHasher(),
                currentUser,
                new AuditService(context, currentUser, DatabaseFixture.Clock),
                DatabaseFixture.Clock);

            await handler.Handle(
                new ChangePasswordCommand(DatabaseSeeder.AdminPassword, "Rotated#Dev12345!"),
                CancellationToken.None);
        }

        await using var verify = _fixture.CreateContext();
        (await verify.AuditLogs.AsNoTracking().CountAsync(entry =>
                entry.Action == AuditActions.PasswordChanged && entry.UserId == userId))
            .Should().Be(1);

        (await verify.RefreshTokens.AsNoTracking().CountAsync(t => t.UserId == userId && t.RevokedAt == null))
            .Should().Be(0, "a password change must invalidate outstanding sessions");
    }

    private LoginCommandHandler CreateLoginHandler(ICurrentUserService? currentUser = null)
    {
        var caller = currentUser ?? new TestCurrentUser();

        // Handler and audit service share one context: the audit service stages
        // onto the caller's unit of work, exactly as production DI scopes it.
        var context = _fixture.CreateContext();
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "ServiceManagement.Tests",
            Audience = "ServiceManagement.Tests.Clients",
            SigningKey = "integration-test-signing-key-that-is-long-enough-1234567890"
        });

        return new LoginCommandHandler(
            context,
            new IdentityPasswordHasher(),
            new JwtTokenService(jwtOptions),
            new AuditService(context, caller, DatabaseFixture.Clock),
            caller,
            DatabaseFixture.Clock,
            Options.Create(new AuthOptions()));
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var context = _fixture.CreateContext();
        return await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .FirstAsync();
    }
}
