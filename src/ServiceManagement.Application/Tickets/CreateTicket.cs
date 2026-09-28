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
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Raises a ticket. Deadlines come from the SLA policy stored in the database,
/// and an optional idempotency key makes retries safe.
/// </summary>
public sealed record CreateTicketCommand(
    string Title,
    string Description,
    Guid ServiceId,
    Guid CategoryId,
    Guid? PriorityId,
    Guid? RequesterId,
    Guid? AssignedTeamId,
    string? IdempotencyKey) : IRequest<TicketDetailDto>;

public sealed class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketCommandValidator()
    {
        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(300);

        RuleFor(command => command.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(8000);

        RuleFor(command => command.ServiceId).NotEmpty().WithMessage("Service is required.");
        RuleFor(command => command.CategoryId).NotEmpty().WithMessage("Category is required.");
    }
}

public sealed class CreateTicketCommandHandler : IRequestHandler<CreateTicketCommand, TicketDetailDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ITicketNumberGenerator _ticketNumberGenerator;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly SlaPolicyResolver _slaPolicyResolver;

    public CreateTicketCommandHandler(
        IApplicationDbContext dbContext,
        ITicketNumberGenerator ticketNumberGenerator,
        INotificationService notificationService,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        SlaPolicyResolver slaPolicyResolver)
    {
        _dbContext = dbContext;
        _ticketNumberGenerator = ticketNumberGenerator;
        _notificationService = notificationService;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
        _slaPolicyResolver = slaPolicyResolver;
    }

    public Task<TicketDetailDto> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
        => _dbContext.ExecuteAsync(
            async token => await CreateAsync(request, token),
            cancellationToken);

    private async Task<TicketDetailDto> CreateAsync(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        // Idempotent replay: the same key never produces a second ticket.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var key = request.IdempotencyKey.Trim();

            var existingTicketId = await _dbContext.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.IdempotencyKey == key)
                .Select(ticket => (Guid?)ticket.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingTicketId is not null)
            {
                return await LoadDetailAsync(existingTicketId.Value, cancellationToken);
            }
        }

        var service = await _dbContext.Services
            .FirstOrDefaultAsync(candidate => candidate.Id == request.ServiceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Service), request.ServiceId);

        var categoryExists = await _dbContext.ServiceCategories
            .AnyAsync(category => category.Id == request.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            throw new NotFoundException(nameof(ServiceCategory), request.CategoryId);
        }

        var priorityId = request.PriorityId ?? service.DefaultPriorityId;

        if (priorityId is null)
        {
            priorityId = await _dbContext.ServicePriorities
                .Where(priority => priority.IsActive)
                .OrderBy(priority => priority.Level)
                .Select(priority => (Guid?)priority.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (priorityId is null)
        {
            throw new ConflictException(
                "No ticket priority is configured. Create at least one priority before raising tickets.");
        }

        var priorityExists = await _dbContext.ServicePriorities
            .AnyAsync(priority => priority.Id == priorityId, cancellationToken);

        if (!priorityExists)
        {
            throw new NotFoundException(nameof(ServicePriority), priorityId);
        }

        var requesterId = request.RequesterId ?? _currentUser.RequireUserId();

        if (!await _dbContext.Users.AnyAsync(user => user.Id == requesterId, cancellationToken))
        {
            throw new NotFoundException("User", requesterId);
        }

        if (request.AssignedTeamId is not null &&
            !await _dbContext.Teams.AnyAsync(team => team.Id == request.AssignedTeamId, cancellationToken))
        {
            throw new NotFoundException(nameof(Team), request.AssignedTeamId);
        }

        var now = _clock.UtcNow;
        var ticketNumber = await _ticketNumberGenerator.NextAsync(cancellationToken);

        var ticket = new Ticket(
            ticketNumber,
            request.Title,
            request.Description,
            service.Id,
            request.CategoryId,
            priorityId.Value,
            requesterId,
            now);

        ticket.SetIdempotencyKey(request.IdempotencyKey);

        if (request.AssignedTeamId is not null)
        {
            ticket.AssignToTeam(request.AssignedTeamId.Value, requesterId, "Auto-assigned on creation", now);
        }

        await _slaPolicyResolver.ApplyToAsync(
            ticket,
            priorityId.Value,
            preferredPolicyId: service.DefaultSlaPolicyId,
            cancellationToken);

        _dbContext.Tickets.Add(ticket);

        if (ticket.AssignedTeamId is not null)
        {
            var memberIds = await _dbContext.TeamMembers
                .AsNoTracking()
                .Where(member => member.TeamId == ticket.AssignedTeamId)
                .Select(member => member.UserId)
                .ToListAsync(cancellationToken);

            // Staged onto this unit of work, so the notification cannot outlive a
            // ticket that was never created.
            await _notificationService.NotifyManyAsync(
                memberIds.Select(memberId => new NotificationMessage(
                    memberId,
                    NotificationType.TicketAssigned,
                    "New ticket for your team",
                    $"Ticket {ticket.TicketNumber} '{ticket.Title}' was assigned to your team.",
                    nameof(Ticket),
                    ticket.Id.ToString(),
                    _currentUser.CorrelationId)),
                cancellationToken);
        }

        await _auditService.RecordAsync(
            TicketAuditEntry.For(
                AuditActions.TicketCreated,
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser,
                newValues: AuditJson.Serialize(new
                {
                    ticket.TicketNumber,
                    ticket.Title,
                    ticket.ServiceId,
                    ticket.CategoryId,
                    ticket.PriorityId,
                    ticket.Status
                })),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await LoadDetailAsync(ticket.Id, cancellationToken);
    }

    private Task<TicketDetailDto> LoadDetailAsync(Guid ticketId, CancellationToken cancellationToken)
        => _dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .ToDetailDto()
            .FirstAsync(cancellationToken);
}
