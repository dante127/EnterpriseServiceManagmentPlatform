using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Auth;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Caching;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Users;

/// <summary>Creates a user and grants the requested roles in one transaction.</summary>
public sealed record CreateUserCommand(
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string Password,
    Guid? OrganizationId,
    Guid? DepartmentId,
    IReadOnlyList<string> Roles) : IRequest<UserDto>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().EmailAddress().WithMessage("A valid e-mail address is required.");

        RuleFor(command => command.UserName)
            .NotEmpty().MinimumLength(3).MaximumLength(128);

        RuleFor(command => command.FirstName).NotEmpty().MaximumLength(128);
        RuleFor(command => command.LastName).NotEmpty().MaximumLength(128);

        RuleFor(command => command.Password)
            .NotEmpty()
            .MinimumLength(12)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a non-alphanumeric character.");

        RuleFor(command => command.Roles)
            .NotEmpty().WithMessage("At least one role must be assigned.");
    }
}

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _clock;

    public CreateUserCommandHandler(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        ICurrentUserService currentUser,
        ICacheService cache,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _currentUser = currentUser;
        _cache = cache;
        _clock = clock;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var emailTaken = await _dbContext.Users
            .AnyAsync(user => user.Email == email, cancellationToken);

        if (emailTaken)
        {
            throw new ConflictException($"A user with e-mail '{email}' already exists.");
        }

        var userNameTaken = await _dbContext.Users
            .AnyAsync(user => user.UserName == request.UserName.Trim(), cancellationToken);

        if (userNameTaken)
        {
            throw new ConflictException($"A user named '{request.UserName}' already exists.");
        }

        var requestedRoleNames = request.Roles
            .Select(role => role.Trim())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roles = await _dbContext.Roles
            .Where(role => requestedRoleNames.Contains(role.Name))
            .ToListAsync(cancellationToken);

        var unknownRoles = requestedRoleNames
            .Where(name => roles.All(role => !string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unknownRoles.Count > 0)
        {
            throw new NotFoundException($"Unknown role(s): {string.Join(", ", unknownRoles)}.");
        }

        var user = new User(
            email,
            request.UserName.Trim(),
            request.FirstName.Trim(),
            request.LastName.Trim(),
            _passwordHasher.Hash(request.Password));

        user.AssignToOrganization(request.OrganizationId, request.DepartmentId);

        foreach (var role in roles)
        {
            user.AddRole(role.Id);
        }

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.RemoveAsync(CacheKeys.UserPermissions(user.Id), cancellationToken);
        await _cache.RemoveAsync(CacheKeys.UserRoles(user.Id), cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.UserCreated,
                nameof(User),
                user.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                NewValues: AuditJson.Serialize(new { user.Email, user.UserName, Roles = roles.Select(r => r.Name) })),
            cancellationToken);

        return new UserDto(
            user.Id,
            user.Email,
            user.UserName,
            user.FirstName,
            user.LastName,
            user.FullName,
            user.IsActive,
            user.OrganizationId,
            user.DepartmentId,
            _clock.UtcNow,
            user.LastLoginAt,
            roles.Select(role => role.Name).ToList());
    }
}

// Login path shares the same projection shape.
