using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Users;

/// <summary>Activates or deactivates an account. Deactivation rotates the security stamp.</summary>
public sealed record SetUserActivationCommand(Guid UserId, bool IsActive) : IRequest<Unit>;

public sealed class SetUserActivationCommandValidator : AbstractValidator<SetUserActivationCommand>
{
    public SetUserActivationCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
    }
}

public sealed class SetUserActivationCommandHandler : IRequestHandler<SetUserActivationCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public SetUserActivationCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(SetUserActivationCommand request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            if (user.Id == _currentUser.UserId)
            {
                throw new ConflictException("You cannot deactivate your own account.");
            }

            user.Deactivate();

            // A deactivated account must not keep working sessions.
            var activeTokens = await _dbContext.RefreshTokens
                .Where(token => token.UserId == user.Id && token.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.Revoke(_clock.UtcNow, _currentUser.IpAddress, "AccountDeactivated");
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                request.IsActive ? AuditActions.UserActivated : AuditActions.UserDeactivated,
                nameof(User),
                user.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        return Unit.Value;
    }
}
