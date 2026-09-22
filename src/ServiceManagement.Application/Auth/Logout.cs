using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Application.Auth;

/// <summary>
/// Revokes a refresh token. Deliberately idempotent: signing out twice, or with
/// a token that never existed, still succeeds.
/// </summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Unit>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(command => command.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public LogoutCommandHandler(
        IApplicationDbContext dbContext,
        IJwtTokenService jwtTokenService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var hash = _jwtTokenService.HashRefreshToken(request.RefreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (storedToken is not null && storedToken.RevokedAt is null)
        {
            storedToken.Revoke(_clock.UtcNow, _currentUser.IpAddress, "Logout");
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
