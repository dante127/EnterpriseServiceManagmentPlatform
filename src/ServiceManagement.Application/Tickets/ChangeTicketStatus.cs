using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Persistence;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

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

    public Task<TicketDetailDto> Handle(
        ChangeTicketStatusCommand request,
        CancellationToken cancellationToken)
        => _dbContext.ExecuteAsync(
            async token =>
            {
                var ticket = await _dbContext.Tickets
                    .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, token)
                    ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

                TicketConcurrency.EnsureUnchanged(ticket, request.RowVersion);

                var changedBy = _currentUser.RequireUserId();
                var previousStatus = ticket.Status;

                ticket.ChangeStatus(request.Status, changedBy, _clock.UtcNow);

                var recipients = TicketNotificationRules.ForStatusChange(ticket, changedBy);

                // Staged onto this unit of work, so neither the notification nor the
                // audit record can survive a failure of the change that caused them.
                await _notificationService.NotifyManyAsync(
                    recipients.Select(userId => new NotificationMessage(
                        userId,
                        NotificationType.TicketStatusChanged,
                        "Ticket status changed",
                        $"Ticket {ticket.TicketNumber} moved from {previousStatus} to {ticket.Status}.",
                        nameof(Ticket),
                        ticket.Id.ToString(),
                        _currentUser.CorrelationId)),
                    token);

                await _auditService.RecordAsync(
                    TicketAuditEntry.For(
                        AuditActions.TicketStatusChanged,
                        nameof(Ticket),
                        ticket.Id.ToString(),
                        _currentUser,
                        userId: changedBy,
                        oldValues: AuditJson.Serialize(new { Status = previousStatus.ToString() }),
                        newValues: AuditJson.Serialize(new { Status = ticket.Status.ToString() })),
                    token);

                await _dbContext.SaveChangesAsync(token);

                return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, token);
            },
            cancellationToken);
}
