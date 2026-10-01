using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Auth;

/// <summary>
/// Rotates a refresh token: the presented token is revoked and replaced by a
/// brand new one, so a stolen token can be used at most once.
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthTokenDto>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokenDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly AuthOptions _authOptions;

    public RefreshTokenCommandHandler(
        IApplicationDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IOptions<AuthOptions> authOptions)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
        _authOptions = authOptions.Value;
    }

    public async Task<AuthTokenDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var presentedHash = _jwtTokenService.HashRefreshToken(request.RefreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == presentedHash, cancellationToken);

        if (storedToken is null)
        {
            throw new UnauthorizedException("The refresh token is not recognised.");
        }

        if (storedToken.IsActive(now))
        {
            return await RotateAsync(storedToken, now, cancellationToken);
        }

        // Replaying a revoked or expired token is treated as a compromise signal:
        // every active token of that user is invalidated.
        await RevokeEveryActiveTokenAsync(storedToken.UserId, "ReuseDetected", cancellationToken);

        throw new UnauthorizedException("The refresh token is no longer valid. Please sign in again.");
    }

    private async Task<AuthTokenDto> RotateAsync(RefreshToken storedToken, DateTimeOffset now, CancellationToken cancellationToken)
    {
        storedToken.User.EnsureActive();

        var securityProfile = await UserSecurityQueryService.LoadAsync(_dbContext, storedToken.UserId, cancellationToken);
        var accessToken = _jwtTokenService.CreateAccessToken(
            storedToken.User,
            securityProfile.Roles,
            securityProfile.Permissions);

        var newRefreshTokenValue = _jwtTokenService.CreateRefreshToken();
        var newRefreshTokenHash = _jwtTokenService.HashRefreshToken(newRefreshTokenValue);
        var refreshTokenExpiresAt = now.AddDays(_authOptions.RefreshTokenDays);

        // Compare-and-swap revoke: the update only lands if the token is still
        // unrevoked at write time. Two concurrent refreshes with the same token
        // would both pass the IsActive check, and without this guard both would
        // mint a valid replacement - a stolen token would no longer be single-use.
        // The loser of the race falls into the reuse path below.
        var revoked = await _dbContext.RefreshTokens
            .Where(token => token.Id == storedToken.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(token => token.RevokedAt, (DateTimeOffset?)now)
                .SetProperty(token => token.RevokedByIp, _currentUser.IpAddress)
                .SetProperty(token => token.ReasonRevoked, "Rotated")
                .SetProperty(token => token.ReplacedByTokenHash, newRefreshTokenHash),
                cancellationToken);

        if (revoked == 0)
        {
            await RevokeEveryActiveTokenAsync(storedToken.UserId, "ReuseDetected", cancellationToken);

            throw new UnauthorizedException("The refresh token is no longer valid. Please sign in again.");
        }

        _dbContext.RefreshTokens.Add(new RefreshToken(
            storedToken.UserId,
            newRefreshTokenHash,
            now,
            refreshTokenExpiresAt,
            _currentUser.IpAddress));

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TokenRefreshed,
                nameof(RefreshToken),
                storedToken.Id.ToString(),
                storedToken.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(
            accessToken.Token,
            accessToken.ExpiresAt,
            newRefreshTokenValue,
            refreshTokenExpiresAt,
            new UserSessionDto(
                storedToken.User.Id,
                storedToken.User.Email,
                storedToken.User.UserName,
                storedToken.User.FullName,
                securityProfile.Roles,
                securityProfile.Permissions));
    }

    private async Task RevokeEveryActiveTokenAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        // Load-then-revoke is safe here: the set only shrinks under concurrency,
        // and a second pass re-revokes tokens that are already revoked.
        var activeTokens = await _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(_clock.UtcNow, _currentUser.IpAddress, reason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
