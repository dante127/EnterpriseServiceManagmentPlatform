using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Users;

/// <summary>Replaces the role set of a user.</summary>
public sealed record AssignUserRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : IRequest<Unit>;

public sealed class AssignUserRolesCommandValidator : AbstractValidator<AssignUserRolesCommand>
{
    public AssignUserRolesCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Roles).NotEmpty().WithMessage("At least one role must be assigned.");
    }
}

public sealed class AssignUserRolesCommandHandler : IRequestHandler<AssignUserRolesCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly ICacheService _cache;

    public AssignUserRolesCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser,
        ICacheService cache)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
        _cache = cache;
    }

    public async Task<Unit> Handle(AssignUserRolesCommand request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), request.UserId);

        var requested = request.Roles
            .Select(role => role.Trim())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roles = await _dbContext.Roles
            .Where(role => requested.Contains(role.Name))
            .ToListAsync(cancellationToken);

        if (roles.Count != requested.Count)
        {
            var unknown = requested
                .Where(name => roles.All(role => !string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            throw new NotFoundException($"Unknown role(s): {string.Join(", ", unknown)}.");
        }

        var previousRoles = await _dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == user.Id)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        foreach (var existing in user.UserRoles.ToList())
        {
            user.RemoveRole(existing.RoleId);
        }

        foreach (var role in roles)
        {
            user.AddRole(role.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Permission lookups are cached, so the cache must be invalidated.
        await _cache.RemoveAsync(Common.Caching.CacheKeys.UserPermissions(user.Id), cancellationToken);
        await _cache.RemoveAsync(Common.Caching.CacheKeys.UserRoles(user.Id), cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.UserRoleChanged,
                nameof(User),
                user.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                Common.Auditing.AuditJson.Serialize(new { Roles = previousRoles }),
                Common.Auditing.AuditJson.Serialize(new { Roles = roles.Select(role => role.Name) })),
            cancellationToken);

        return Unit.Value;
    }
}
