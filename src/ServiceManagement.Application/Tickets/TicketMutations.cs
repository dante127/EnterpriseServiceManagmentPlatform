using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>Updates ticket details and (optionally) its priority, recalculating SLA targets.</summary>
public sealed record UpdateTicketCommand(
    Guid TicketId,
    string Title,
    string Description,
    Guid CategoryId,
    Guid? PriorityId,
    string? RowVersion) : IRequest<TicketDetailDto>;

public sealed class UpdateTicketCommandValidator : AbstractValidator<UpdateTicketCommand>
{
    public UpdateTicketCommandValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
        RuleFor(command => command.Title).NotEmpty().MaximumLength(300);
        RuleFor(command => command.Description).NotEmpty().MaximumLength(8000);
        RuleFor(command => command.CategoryId).NotEmpty();
    }
}

public sealed class UpdateTicketCommandHandler : IRequestHandler<UpdateTicketCommand, TicketDetailDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public UpdateTicketCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<TicketDetailDto> Handle(UpdateTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        TicketConcurrency.EnsureUnchanged(ticket, request.RowVersion);

        var previous = new { ticket.Title, ticket.Description, ticket.CategoryId, ticket.PriorityId };

        ticket.UpdateDetails(request.Title, request.Description, request.CategoryId, request.PriorityId ?? ticket.PriorityId);

        if (request.PriorityId is not null && request.PriorityId != previous.PriorityId)
        {
            await RecalculateSlaAsync(ticket, request.PriorityId.Value, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TicketUpdated,
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                AuditJson.Serialize(previous),
                AuditJson.Serialize(new { ticket.Title, ticket.Description, ticket.CategoryId, ticket.PriorityId })),
            cancellationToken);

        return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, cancellationToken);
    }

    private async Task RecalculateSlaAsync(Ticket ticket, Guid priorityId, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.SlaPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.PriorityId == priorityId && candidate.IsActive,
                cancellationToken);

        if (policy is null)
        {
            return;
        }

        ticket.ApplySlaPolicy(
            policy.FirstResponseMinutes,
            policy.ResolutionMinutes,
            policy.AtRiskThresholdPercent);
    }
}

/// <summary>Assigns or reassigns a ticket to a user and/or a team.</summary>
public sealed record AssignTicketCommand(
    Guid TicketId,
    Guid? AssigneeId,
    Guid? TeamId,
    string? Reason,
    string? RowVersion) : IRequest<TicketDetailDto>;

public sealed class AssignTicketCommandValidator : AbstractValidator<AssignTicketCommand>
{
    public AssignTicketCommandValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();

        RuleFor(command => command)
            .Must(command => command.AssigneeId is not null || command.TeamId is not null)
            .WithMessage("Either an assignee or a team must be supplied.");

        RuleFor(command => command.Reason).MaximumLength(500);
    }
}

public sealed class AssignTicketCommandHandler : IRequestHandler<AssignTicketCommand, TicketDetailDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public AssignTicketCommandHandler(
        IApplicationDbContext dbContext,
        INotificationService notificationService,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<TicketDetailDto> Handle(AssignTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        TicketConcurrency.EnsureUnchanged(ticket, request.RowVersion);

        var changedBy = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var wasAssigned = ticket.AssignedToId is not null || ticket.AssignedTeamId is not null;
        var previousAssigneeId = ticket.AssignedToId;

        if (request.AssigneeId is not null)
        {
            var assigneeExists = await _dbContext.Users
                .AnyAsync(user => user.Id == request.AssigneeId && user.IsActive, cancellationToken);

            if (!assigneeExists)
            {
                throw new NotFoundException("User", request.AssigneeId);
            }

            ticket.AssignToUser(request.AssigneeId.Value, changedBy, request.Reason, now);
        }

        if (request.TeamId is not null)
        {
            var teamExists = await _dbContext.Teams
                .AnyAsync(team => team.Id == request.TeamId, cancellationToken);

            if (!teamExists)
            {
                throw new NotFoundException(nameof(Team), request.TeamId);
            }

            ticket.AssignToTeam(request.TeamId.Value, changedBy, request.Reason, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var notificationType = wasAssigned
            ? NotificationType.TicketReassigned
            : NotificationType.TicketAssigned;

        var recipients = new List<Guid>();

        if (ticket.AssignedToId is not null)
        {
            recipients.Add(ticket.AssignedToId.Value);
        }
        else if (ticket.AssignedTeamId is not null)
        {
            var memberIds = await _dbContext.TeamMembers
                .AsNoTracking()
                .Where(member => member.TeamId == ticket.AssignedTeamId)
                .Select(member => member.UserId)
                .ToListAsync(cancellationToken);

            recipients.AddRange(memberIds);
        }

        var targets = recipients
            .Where(userId => userId != changedBy)
            .Distinct()
            .ToList();

        await _notificationService.NotifyManyAsync(
            targets.Select(userId => new NotificationMessage(
                userId,
                notificationType,
                notificationType == NotificationType.TicketReassigned ? "Ticket reassigned" : "Ticket assigned",
                $"Ticket {ticket.TicketNumber} '{ticket.Title}' was "
                    + (notificationType == NotificationType.TicketReassigned ? "reassigned" : "assigned")
                    + " to you.",
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser.CorrelationId)),
            cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TicketAssigned,
                nameof(Ticket),
                ticket.Id.ToString(),
                changedBy,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                AuditJson.Serialize(new { AssignedToId = previousAssigneeId }),
                AuditJson.Serialize(new { ticket.AssignedToId, ticket.AssignedTeamId, request.Reason })),
            cancellationToken);

        return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, cancellationToken);
    }
}

/// <summary>Moves a ticket through the controlled status workflow.</summary>
public sealed record ChangeTicketStatusCommand(
    Guid TicketId,
    TicketStatus Status,
    string? RowVersion) : IRequest<TicketDetailDto>;

public sealed class ChangeTicketStatusCommandValidator : AbstractValidator<ChangeTicketStatusCommand>
{
    public ChangeTicketStatusCommandValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
        RuleFor(command => command.Status).IsInEnum();
    }
}

public sealed class ChangeTicketStatusCommandHandler : IRequestHandler<ChangeTicketStatusCommand, TicketDetailDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public ChangeTicketStatusCommandHandler(
        IApplicationDbContext dbContext,
        INotificationService notificationService,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<TicketDetailDto> Handle(
        ChangeTicketStatusCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        TicketConcurrency.EnsureUnchanged(ticket, request.RowVersion);

        var changedBy = _currentUser.RequireUserId();
        var previousStatus = ticket.Status;

        ticket.ChangeStatus(request.Status, changedBy, _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var recipients = new[] { ticket.RequesterId, ticket.AssignedToId ?? Guid.Empty }
            .Where(userId => userId != Guid.Empty && userId != changedBy)
            .Distinct()
            .ToList();

        await _notificationService.NotifyManyAsync(
            recipients.Select(userId => new NotificationMessage(
                userId,
                NotificationType.TicketStatusChanged,
                "Ticket status changed",
                $"Ticket {ticket.TicketNumber} moved from {previousStatus} to {ticket.Status}.",
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser.CorrelationId)),
            cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TicketStatusChanged,
                nameof(Ticket),
                ticket.Id.ToString(),
                changedBy,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                AuditJson.Serialize(new { Status = previousStatus.ToString() }),
                AuditJson.Serialize(new { Status = ticket.Status.ToString() })),
            cancellationToken);

        return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, cancellationToken);
    }
}

/// <summary>Soft-deletes a ticket. The row is retained for auditing.</summary>
public sealed record DeleteTicketCommand(Guid TicketId) : IRequest<Unit>;

public sealed class DeleteTicketCommandValidator : AbstractValidator<DeleteTicketCommand>
{
    public DeleteTicketCommandValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
    }
}

public sealed class DeleteTicketCommandHandler : IRequestHandler<DeleteTicketCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public DeleteTicketCommandHandler(
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

    public async Task<Unit> Handle(DeleteTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _dbContext.Tickets
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        ticket.IsDeleted = true;
        ticket.DeletedAt = _clock.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TicketDeleted,
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                AuditJson.Serialize(new { ticket.TicketNumber, ticket.Title, Status = ticket.Status.ToString() })),
            cancellationToken);

        return Unit.Value;
    }
}
