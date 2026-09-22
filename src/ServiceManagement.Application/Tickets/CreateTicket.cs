using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
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
    private readonly SlaOptions _slaOptions;

    public CreateTicketCommandHandler(
        IApplicationDbContext dbContext,
        ITicketNumberGenerator ticketNumberGenerator,
        INotificationService notificationService,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IOptions<SlaOptions> slaOptions)
    {
        _dbContext = dbContext;
        _ticketNumberGenerator = ticketNumberGenerator;
        _notificationService = notificationService;
        _auditService = auditService;
        _currentUser = currentUser;
        _clock = clock;
        _slaOptions = slaOptions.Value;
    }

    public async Task<TicketDetailDto> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
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

        await ApplySlaPolicyAsync(ticket, service, priorityId.Value, cancellationToken);

        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (ticket.AssignedTeamId is not null)
        {
            var memberIds = await _dbContext.TeamMembers
                .AsNoTracking()
                .Where(member => member.TeamId == ticket.AssignedTeamId)
                .Select(member => member.UserId)
                .ToListAsync(cancellationToken);

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
            new AuditEntry(
                AuditActions.TicketCreated,
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser.UserId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                NewValues: AuditJson.Serialize(new
                {
                    ticket.TicketNumber,
                    ticket.Title,
                    ticket.ServiceId,
                    ticket.CategoryId,
                    ticket.PriorityId,
                    ticket.Status
                })),
            cancellationToken);

        return await LoadDetailAsync(ticket.Id, cancellationToken);
    }

    private async Task ApplySlaPolicyAsync(
        Ticket ticket,
        Service service,
        Guid priorityId,
        CancellationToken cancellationToken)
    {
        SlaPolicy? policy = null;

        if (service.DefaultSlaPolicyId is not null)
        {
            policy = await _dbContext.SlaPolicies
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.Id == service.DefaultSlaPolicyId && candidate.IsActive,
                    cancellationToken);
        }

        policy ??= await _dbContext.SlaPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.PriorityId == priorityId && candidate.IsActive,
                cancellationToken);

        if (policy is not null)
        {
            ticket.ApplySlaPolicy(
                policy.FirstResponseMinutes,
                policy.ResolutionMinutes,
                policy.AtRiskThresholdPercent);
            return;
        }

        // No policy configured: documented fallback targets keep the ticket measurable.
        ticket.ApplySlaPolicy(
            _slaOptions.FallbackFirstResponseMinutes,
            _slaOptions.FallbackResolutionMinutes,
            _slaOptions.AtRiskThresholdPercent);
    }

    private Task<TicketDetailDto> LoadDetailAsync(Guid ticketId, CancellationToken cancellationToken)
        => _dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == ticketId)
            .ToDetailDto()
            .FirstAsync(cancellationToken);
}
