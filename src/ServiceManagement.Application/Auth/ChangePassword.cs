using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Auth;

/// <summary>
/// Changes the password of the authenticated caller and invalidates every
/// outstanding refresh token, forcing other sessions to sign in again.
/// </summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Unit>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public const int MinimumPasswordLength = 12;

    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(command => command.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(MinimumPasswordLength)
                .WithMessage($"New password must be at least {MinimumPasswordLength} characters long.")
            .Matches("[A-Z]").WithMessage("New password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("New password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("New password must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("New password must contain a non-alphanumeric character.");

        RuleFor(command => command.NewPassword)
            .NotEqual(command => command.CurrentPassword)
            .WithMessage("New password must differ from the current password.");
    }
}

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IDateTimeProvider _clock;

    public ChangePasswordCommandHandler(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _auditService = auditService;
        _clock = clock;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.RequireUserId();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        if (!_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            throw new UnauthorizedException("The current password is incorrect.");
        }

        user.SetPasswordHash(_passwordHasher.Hash(request.NewPassword));

        var activeTokens = await _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(_clock.UtcNow, _currentUser.IpAddress, "PasswordChanged");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.PasswordChanged,
                nameof(User),
                user.Id.ToString(),
                user.Id,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        return Unit.Value;
    }
}
