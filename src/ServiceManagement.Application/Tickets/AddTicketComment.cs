using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Adds a comment. Internal comments require the
/// <c>tickets.comment.internal</c> permission, enforced here as well as at the
/// controller so the rule cannot be bypassed by a new endpoint.
/// </summary>
public sealed record AddTicketCommentCommand(Guid TicketId, string Content, bool IsInternal)
    : IRequest<TicketCommentDto>;

public sealed class AddTicketCommentCommandValidator : AbstractValidator<AddTicketCommentCommand>
{
    public AddTicketCommentCommandValidator()
    {
        RuleFor(command => command.TicketId).NotEmpty();
        RuleFor(command => command.Content).NotEmpty().MaximumLength(4000);
    }
}

public sealed class AddTicketCommentCommandHandler : IRequestHandler<AddTicketCommentCommand, TicketCommentDto>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public AddTicketCommentCommandHandler(
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

    public async Task<TicketCommentDto> Handle(
        AddTicketCommentCommand request,
        CancellationToken cancellationToken)
    {
        if (!TicketAccessPolicy.CanCreateInternalComment(request.IsInternal, _currentUser.Permissions))
        {
            throw new ForbiddenException("You are not allowed to create internal comments.");
        }

        var ticket = await _dbContext.Tickets
            .FirstOrDefaultAsync(candidate => candidate.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ticket), request.TicketId);

        var authorId = _currentUser.RequireUserId();
        var comment = ticket.AddComment(request.Content, authorId, request.IsInternal, _clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Internal notes must not leak to the requester.
        var recipients = new List<Guid>();
        if (!request.IsInternal)
        {
            recipients.Add(ticket.RequesterId);
        }

        if (ticket.AssignedToId is not null)
        {
            recipients.Add(ticket.AssignedToId.Value);
        }

        var targets = recipients
            .Where(userId => userId != authorId)
            .Distinct()
            .ToList();

        await _notificationService.NotifyManyAsync(
            targets.Select(userId => new NotificationMessage(
                userId,
                NotificationType.TicketCommentAdded,
                "New comment on a ticket",
                $"A new comment was added to ticket {ticket.TicketNumber}.",
                nameof(Ticket),
                ticket.Id.ToString(),
                _currentUser.CorrelationId)),
            cancellationToken);

        await _auditService.RecordAsync(
            new AuditEntry(
                AuditActions.TicketCommentAdded,
                nameof(TicketComment),
                comment.Id.ToString(),
                authorId,
                _currentUser.IpAddress,
                _currentUser.CorrelationId,
                NewValues: AuditJson.Serialize(new { comment.TicketId, IsInternal = comment.IsInternal })),
            cancellationToken);

        var author = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == authorId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown";

        return new TicketCommentDto(
            comment.Id,
            comment.TicketId,
            comment.AuthorId,
            author,
            comment.Content,
            comment.IsInternal,
            comment.CreatedAt,
            comment.UpdatedAt);
    }
}
