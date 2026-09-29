using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Auditing;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Persistence;
using ServiceManagement.Application.Common.Security;
using ServiceManagement.Domain.Auditing;
using ServiceManagement.Domain.Notifications;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Adds a comment. Internal comments require the
/// <c>tickets.comment.internal</c> permission, enforced here as well as at the
/// controller so the rule cannot be bypassed by a new endpoint.
/// <para>
/// Permitted on a closed or cancelled ticket: annotating a finished ticket is a
/// deliberate feature, unlike every other mutation. See <c>Ticket.AddComment</c>
/// for the reasoning.
/// </para>
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

    public Task<TicketCommentDto> Handle(
        AddTicketCommentCommand request,
        CancellationToken cancellationToken)
        => _dbContext.ExecuteAsync(
            async token => await AddAsync(request, token),
            cancellationToken);

    private async Task<TicketCommentDto> AddAsync(
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

        // A comment on an already-persisted ticket would be tracked as Modified
        // (ids are generated client-side, so EF cannot detect the new row) and the
        // UPDATE would match nothing, surfacing as a false concurrency conflict.
        _dbContext.TicketComments.Add(comment);

        // Internal notes must not leak to the requester; the rule lives in
        // one place so comment and status-change notifications cannot drift.
        var targets = TicketNotificationRules.ForComment(ticket, authorId, request.IsInternal);

        // Staged onto this unit of work, so neither the notification nor the audit
        // record can survive a failure of the comment that caused them.
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
            TicketAuditEntry.For(
                AuditActions.TicketCommentAdded,
                nameof(TicketComment),
                comment.Id.ToString(),
                _currentUser,
                userId: authorId,
                newValues: AuditJson.Serialize(new { comment.TicketId, IsInternal = comment.IsInternal })),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

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
