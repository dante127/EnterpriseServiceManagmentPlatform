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
    private readonly SlaPolicyResolver _slaPolicyResolver;

    public UpdateTicketCommandHandler(
        IApplicationDbContext dbContext,
        IAuditService auditService,
        ICurrentUserService currentUser,
        SlaPolicyResolver slaPolicyResolver)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
        _slaPolicyResolver = slaPolicyResolver;
    }

    public Task<TicketDetailDto> Handle(UpdateTicketCommand request, CancellationToken cancellationToken)
        => _dbContext.ExecuteAsync(
            async token =>
            {
                var ticket = await _dbContext.Tickets
                    .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, token)
                    ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

                TicketConcurrency.EnsureUnchanged(ticket, request.RowVersion);

                var previous = new { ticket.Title, ticket.Description, ticket.CategoryId, ticket.PriorityId };

                // UpdateDetails only invalidates the SLA when the priority actually
                // changed, so an ordinary edit keeps its deadlines.
                ticket.UpdateDetails(
                    request.Title,
                    request.Description,
                    request.CategoryId,
                    request.PriorityId ?? ticket.PriorityId);

                if (request.PriorityId is not null && request.PriorityId != previous.PriorityId)
                {
                    await _slaPolicyResolver.ApplyToAsync(
                        ticket,
                        request.PriorityId.Value,
                        preferredPolicyId: null,
                        token);
                }

                // Staged, not yet written: the audit record joins the same unit of work.
                await _auditService.RecordAsync(
                    TicketAuditEntry.For(
                        AuditActions.TicketUpdated,
                        nameof(Ticket),
                        ticket.Id.ToString(),
                        _currentUser,
                        oldValues: AuditJson.Serialize(previous),
                        newValues: AuditJson.Serialize(new { ticket.Title, ticket.Description, ticket.CategoryId, ticket.PriorityId })),
                    token);

                await _dbContext.SaveChangesAsync(token);

                return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, token);
            },
            cancellationToken);
}
