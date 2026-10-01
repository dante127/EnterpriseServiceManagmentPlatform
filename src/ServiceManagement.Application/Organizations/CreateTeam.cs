using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Application.Organizations;

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
                .AnyAsync(user => user.Id == request.ManagerId && user.IsActive, cancellationToken);

            if (!managerExists)
            {
                throw new NotFoundException("User", request.ManagerId);
            }

            team.SetManager(request.ManagerId);
        }

        _dbContext.Teams.Add(team);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TeamCreated,
                nameof(Team),
                team.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

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
