using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Organizations;

namespace ServiceManagement.Application.Organizations;

/// <summary>Adds a user to a team, optionally as team manager.</summary>
public sealed record AddTeamMemberCommand(Guid TeamId, Guid UserId, bool IsManager) : IRequest<TeamMemberDto>;

public sealed class AddTeamMemberCommandValidator : AbstractValidator<AddTeamMemberCommand>
{
    public AddTeamMemberCommandValidator()
    {
        RuleFor(command => command.TeamId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
    }
}

public sealed class AddTeamMemberCommandHandler : IRequestHandler<AddTeamMemberCommand, TeamMemberDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public AddTeamMemberCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<TeamMemberDto> Handle(AddTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var team = await _dbContext.Teams
            .Include(candidate => candidate.Members)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TeamId, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), request.TeamId);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var member = team.AddMember(user.Id, request.IsManager);

        if (request.IsManager)
        {
            team.SetManager(user.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TeamMemberAdded,
                nameof(Team),
                team.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                NewValues: Common.Auditing.AuditJson.Serialize(new { UserId = user.Id, user.Email, IsManager = request.IsManager })),
            cancellationToken);

        return new TeamMemberDto(user.Id, user.FullName, user.Email, member.IsManager, member.JoinedAt);
    }
}

/// <summary>Removes a user from a team. Idempotent.</summary>
public sealed record RemoveTeamMemberCommand(Guid TeamId, Guid UserId) : IRequest<Unit>;

public sealed class RemoveTeamMemberCommandValidator : AbstractValidator<RemoveTeamMemberCommand>
{
    public RemoveTeamMemberCommandValidator()
    {
        RuleFor(command => command.TeamId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
    }
}

public sealed class RemoveTeamMemberCommandHandler : IRequestHandler<RemoveTeamMemberCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public RemoveTeamMemberCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(RemoveTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var team = await _dbContext.Teams
            .Include(candidate => candidate.Members)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TeamId, cancellationToken)
            ?? throw new NotFoundException(nameof(Team), request.TeamId);

        team.RemoveMember(request.UserId);

        if (team.ManagerId == request.UserId)
        {
            team.SetManager(null);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TeamMemberRemoved,
                nameof(Team),
                team.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId),
            cancellationToken);

        return Unit.Value;
    }
}
