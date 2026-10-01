using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Persistence;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

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

    public Task<TicketDetailDto> Handle(AssignTicketCommand request, CancellationToken cancellationToken)
        => _dbContext.ExecuteAsync(
            async token =>
            {
                var ticket = await _dbContext.Tickets
                    .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, token)
                    ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

                TicketConcurrency.EnsureUnchanged(ticket, request.RowVersion);

                var changedBy = _currentUser.RequireUserId();
                var now = _clock.UtcNow;

                var wasAssigned = ticket.AssignedToId is not null || ticket.AssignedTeamId is not null;
                var previousAssigneeId = ticket.AssignedToId;
                var previousTeamId = ticket.AssignedTeamId;
                var assignments = new List<TicketAssignmentHistory>();

                if (request.AssigneeId is not null)
                {
                    var assigneeExists = await _dbContext.Users
                        .AnyAsync(user => user.Id == request.AssigneeId && user.IsActive, token);

                    if (!assigneeExists)
                    {
                        throw new NotFoundException("User", request.AssigneeId);
                    }

                    assignments.Add(ticket.AssignToUser(request.AssigneeId.Value, changedBy, request.Reason, now));
                }

                if (request.TeamId is not null)
                {
                    var teamExists = await _dbContext.Teams
                        .AnyAsync(team => team.Id == request.TeamId, token);

                    if (!teamExists)
                    {
                        throw new NotFoundException(nameof(Team), request.TeamId);
                    }

                    assignments.Add(ticket.AssignToTeam(request.TeamId.Value, changedBy, request.Reason, now));
                }

                // Trail rows are children of an already-persisted ticket. Because ids
                // are generated client-side, EF cannot tell them apart from stored rows
                // and would issue an UPDATE that matches nothing, reported as a false
                // concurrency conflict. They must be added explicitly - and every row
                // kept: a request may assign a user and a team in one call.
                foreach (var assignment in assignments)
                {
                    _dbContext.TicketAssignmentHistories.Add(assignment);
                }

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
                        .ToListAsync(token);

                    recipients.AddRange(memberIds);
                }

                var targets = recipients
                    .Where(userId => userId != changedBy)
                    .Distinct()
                    .ToList();

                // Staged onto this unit of work, so neither the notification nor the
                // audit record can survive a failure of the change that caused them.
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
                    token);

                await _auditService.RecordAsync(
                    TicketAuditEntry.For(
                        AuditActions.TicketAssigned,
                        nameof(Ticket),
                        ticket.Id.ToString(),
                        _currentUser,
                        userId: changedBy,
                        oldValues: AuditJson.Serialize(new { AssignedToId = previousAssigneeId, AssignedTeamId = previousTeamId }),
                        newValues: AuditJson.Serialize(new { ticket.AssignedToId, ticket.AssignedTeamId, request.Reason })),
                    token);

                await _dbContext.SaveChangesAsync(token);

                return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, token);
            },
            cancellationToken);
}
