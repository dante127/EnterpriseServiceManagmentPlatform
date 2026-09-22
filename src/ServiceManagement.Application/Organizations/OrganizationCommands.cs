using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Application.Organizations;

public sealed record CreateOrganizationCommand(string Name, string Code, string? Description)
    : IRequest<OrganizationDto>;

public sealed class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class CreateOrganizationCommandHandler
    : IRequestHandler<CreateOrganizationCommand, OrganizationDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public CreateOrganizationCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<OrganizationDto> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var exists = await _dbContext.Organizations
            .AnyAsync(organization => organization.Code == code, cancellationToken);

        if (exists)
        {
            throw new ConflictException($"An organization with code '{code}' already exists.");
        }

        var organization = new Organization(request.Name, code, request.Description);
        _dbContext.Organizations.Add(organization);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.OrganizationCreated,
                nameof(Organization),
                organization.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        return new OrganizationDto(
            organization.Id,
            organization.Name,
            organization.Code,
            organization.Description,
            organization.IsActive,
            0,
            0);
    }
}

public sealed record CreateDepartmentCommand(Guid OrganizationId, string Name, string Code, string? Description)
    : IRequest<DepartmentDto>;

public sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(command => command.OrganizationId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class CreateDepartmentCommandHandler : IRequestHandler<CreateDepartmentCommand, DepartmentDto>
{
    private readonly IApplicationDbContext _dbContext;

    public CreateDepartmentCommandHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DepartmentDto> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var organization = await _dbContext.Organizations
            .Include(candidate => candidate.Departments)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException(nameof(Organization), request.OrganizationId);

        var department = organization.AddDepartment(request.Name, request.Code, request.Description);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DepartmentDto(
            department.Id,
            department.OrganizationId,
            department.Name,
            department.Code,
            department.Description);
    }
}

public sealed record CreateTeamCommand(
    Guid OrganizationId,
    Guid DepartmentId,
    string Name,
    string Code,
    string? Description,
    Guid? ManagerId) : IRequest<TeamDto>;

public sealed class CreateTeamCommandValidator : AbstractValidator<CreateTeamCommand>
{
    public CreateTeamCommandValidator()
    {
        RuleFor(command => command.OrganizationId).NotEmpty();
        RuleFor(command => command.DepartmentId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class CreateTeamCommandHandler : IRequestHandler<CreateTeamCommand, TeamDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public CreateTeamCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<TeamDto> Handle(CreateTeamCommand request, CancellationToken cancellationToken)
    {
        var departmentExists = await _dbContext.Departments
            .AnyAsync(department =>
                department.Id == request.DepartmentId &&
                department.OrganizationId == request.OrganizationId,
                cancellationToken);

        if (!departmentExists)
        {
            throw new NotFoundException(nameof(Department), request.DepartmentId);
        }

        var team = new Team(
            request.OrganizationId,
            request.DepartmentId,
            request.Name,
            request.Code,
            request.Description);

        if (request.ManagerId is not null)
        {
            var managerExists = await _dbContext.Users
                .AnyAsync(user => user.Id == request.ManagerId, cancellationToken);

            if (!managerExists)
            {
                throw new NotFoundException("User", request.ManagerId);
            }

            team.SetManager(request.ManagerId);
        }

        _dbContext.Teams.Add(team);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TeamCreated,
                nameof(Team),
                team.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        return new TeamDto(
            team.Id,
            team.OrganizationId,
            team.DepartmentId,
            team.Name,
            team.Code,
            team.Description,
            team.ManagerId,
            null,
            0);
    }
}
