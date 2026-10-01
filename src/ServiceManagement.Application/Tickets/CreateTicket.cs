using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Persistence;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Constants;
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

        // The column behind the filtered unique index is nvarchar(450); a longer
        // key would otherwise surface as a SQL truncation error, i.e. a 500.
        RuleFor(command => command.IdempotencyKey).MaximumLength(450);
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
        var key = request.IdempotencyKey?.Trim();

        // Idempotent replay: the same key never produces a second ticket.
        if (!string.IsNullOrWhiteSpace(key))
        {
            var existingTicketId = await _dbContext.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.IdempotencyKey == key)
                .Select(ticket => (Guid?)ticket.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingTicketId is not null)
            {
                return await TicketQueries.LoadDetailAsync(_dbContext, existingTicketId.Value, cancellationToken);
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

        // Creating a ticket on behalf of someone else is a privileged action;
        // without this gate any requester could forge tickets attributed to
        // other users. tickets.manage is otherwise granted only to staff roles.
        var requesterId = request.RequesterId is not null && _currentUser.HasPermission(Permissions.TicketsManage)
            ? request.RequesterId.Value
            : _currentUser.RequireUserId();

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

        ticket.SetIdempotencyKey(key);

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

            // The same actor-exclusion and dedupe rules as every other ticket
            // notification: the creator must not be told about their own ticket.
            var targets = TicketNotificationRules.ForTeamAssignment(memberIds, requesterId);

            // Staged onto this unit of work, so the notification cannot outlive a
            // ticket that was never created.
            await _notificationService.NotifyManyAsync(
                targets.Select(memberId => new NotificationMessage(
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // A concurrent retry with the same key won the filtered unique index
            // race. Serve the winner's ticket: surfacing a 500 here would defeat
            // the entire point of the idempotency key.
            var winnerId = await _dbContext.Tickets
                .AsNoTracking()
                .Where(ticket => ticket.IdempotencyKey == key)
                .Select(ticket => (Guid?)ticket.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (winnerId is null)
            {
                throw;
            }

            return await TicketQueries.LoadDetailAsync(_dbContext, winnerId.Value, cancellationToken);
        }

        return await TicketQueries.LoadDetailAsync(_dbContext, ticket.Id, cancellationToken);
    }
}
