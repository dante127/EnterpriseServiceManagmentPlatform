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

/// <summary>Exchanges e-mail and password for an access/refresh token pair.</summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<AuthTokenDto>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid e-mail address.");

        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly AuthOptions _authOptions;

    public LoginCommandHandler(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IOptions<AuthOptions> authOptions)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
        _authOptions = authOptions.Value;
    }

    public async Task<AuthTokenDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == normalizedEmail, cancellationToken);

        // The same generic message is used for unknown users and bad passwords so
        // the endpoint cannot be used to enumerate accounts.
        if (user is null || !_passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            await _auditService.RecordAsync(
                new AuditEntry(
                    AuditActions.LoginFailed,
                    nameof(User),
                    user?.Id.ToString() ?? normalizedEmail,
                    user?.Id,
                    _currentUser.IpAddress,
                    _currentUser.CorrelationId),
                cancellationToken);

            // Nothing else is pending on this failure path, so flush here: without
            // a save the staged audit row dies with the thrown exception.
            await _dbContext.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException("Invalid e-mail or password.");
        }

        user.EnsureActive();

        var now = _clock.UtcNow;
        var securityProfile = await UserSecurityQueryService.LoadAsync(_dbContext, user.Id, cancellationToken);

        var accessToken = _jwtTokenService.CreateAccessToken(user, securityProfile.Roles, securityProfile.Permissions);
        var refreshTokenValue = _jwtTokenService.CreateRefreshToken();
        var refreshTokenExpiresAt = now.AddDays(_authOptions.RefreshTokenDays);

        _dbContext.RefreshTokens.Add(new RefreshToken(
            user.Id,
            _jwtTokenService.HashRefreshToken(refreshTokenValue),
            now,
            refreshTokenExpiresAt,
            _currentUser.IpAddress));

        user.RecordLogin(now);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.Login,
                nameof(User),
                user.Id.ToString(),
                user.Id,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshTokenValue,
            refreshTokenExpiresAt,
            new UserSessionDto(
                user.Id,
                user.Email,
                user.UserName,
                user.FullName,
                securityProfile.Roles,
                securityProfile.Permissions));
    }
}
