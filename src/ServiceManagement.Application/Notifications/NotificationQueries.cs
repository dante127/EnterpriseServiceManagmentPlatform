using FluentValidation;
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
        var (page, pageSize) = PaginationRequest.Normalize(request.Page, request.PageSize);

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId);

        if (request.IsRead is not null)
        {
            query = query.Where(notification => notification.IsRead == request.IsRead);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Projected in the database rather than materialised and adapted in memory,
        // like every other query in the codebase. The enum is converted to its name
        // here, so the client receives "SlaBreached" and the selected columns are
        // exactly the ones the DTO declares - a client never sees a field it did
        // not ask for, and the SQL stays visible when this query is tuned.
        var items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(notification => new NotificationDto(
                notification.Id,
                notification.UserId,
                notification.Type.ToString(),
                notification.Title,
                notification.Message,
                notification.EntityName,
                notification.EntityId,
                notification.IsRead,
                notification.ReadAt,
                notification.CreatedAt))
            .ToListAsync(cancellationToken);

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

        // Set-based update: the whole unread inbox is marked in one statement, so a
        // user with thousands of notifications never materialises them in memory.
        // The rows are not loaded, so the change tracker stays clean.
        return await _dbContext.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(notification => notification.IsRead, true)
                    .SetProperty(notification => notification.ReadAt, now)
                    // ExecuteUpdate bypasses the save interceptor, so the audit
                    // columns are stamped here instead.
                    .SetProperty(notification => notification.UpdatedAt, (DateTimeOffset?)now)
                    .SetProperty(notification => notification.UpdatedBy, _currentUser.UserId),
                cancellationToken);
    }
}
