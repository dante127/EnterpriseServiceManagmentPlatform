using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Persistence;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

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

    public Task<Unit> Handle(DeleteTicketCommand request, CancellationToken cancellationToken)
        => _dbContext.ExecuteAsync(
            async token =>
            {
                var ticket = await _dbContext.Tickets
                    .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, token)
                    ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

                ticket.IsDeleted = true;
                ticket.DeletedAt = _clock.UtcNow;

                // Staged, not yet written: the audit record joins the same unit of
                // work, so a delete can never be applied without its trail.
                await _auditService.RecordAsync(
                    TicketAuditEntry.For(
                        AuditActions.TicketDeleted,
                        nameof(Ticket),
                        ticket.Id.ToString(),
                        _currentUser,
                        newValues: AuditJson.Serialize(new { ticket.TicketNumber, ticket.Title, Status = ticket.Status.ToString() })),
                    token);

                await _dbContext.SaveChangesAsync(token);

                return Unit.Value;
            },
            cancellationToken);
}
