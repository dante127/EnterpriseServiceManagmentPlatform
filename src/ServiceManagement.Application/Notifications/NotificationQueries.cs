using FluentValidation;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ServiceManagement.Application.Common.Exceptions;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Models;
using ServiceManagement.Domain.Notifications;

namespace ServiceManagement.Application.Notifications;

/// <summary>
/// Lists notifications of the authenticated user. Callers can never read
/// somebody else's inbox: the user id comes from the validated token.
/// </summary>
public sealed record GetNotificationsQuery(
    bool? IsRead,
    int Page = 1,
    int PageSize = PaginationRequest.DefaultPageSize) : IRequest<PagedResult<NotificationDto>>;

public sealed class GetNotificationsQueryHandler
    : IRequestHandler<GetNotificationsQuery, PagedResult<NotificationDto>>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public GetNotificationsQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<NotificationDto>> Handle(
        GetNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.RequireUserId();
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, PaginationRequest.MaxPageSize);

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId);

        if (request.IsRead is not null)
        {
            query = query.Where(notification => notification.IsRead == request.IsRead);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var entities = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Mapster keeps the DTO shape decoupled from the entity.
        var items = entities.Select(notification => notification.Adapt<NotificationDto>()).ToList();

        return new PagedResult<NotificationDto>(items, page, pageSize, totalCount);
    }
}

/// <summary>Marks one notification as read. Idempotent.</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : IRequest<Unit>;

public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator()
    {
        RuleFor(command => command.NotificationId).NotEmpty();
    }
}

public sealed class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Unit>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public MarkNotificationReadCommandHandler(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.RequireUserId();

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.NotificationId && candidate.UserId == userId,
                cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), request.NotificationId);

        notification.MarkAsRead(_clock.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Marks the whole inbox of the authenticated user as read.</summary>
public sealed record MarkAllNotificationsReadCommand : IRequest<int>;

public sealed class MarkAllNotificationsReadCommandHandler
    : IRequestHandler<MarkAllNotificationsReadCommand, int>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public MarkAllNotificationsReadCommandHandler(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<int> Handle(MarkAllNotificationsReadCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var unread = await _dbContext.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.MarkAsRead(now);
        }

        if (unread.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return unread.Count;
    }
}
