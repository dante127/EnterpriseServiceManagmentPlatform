using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceManagement.Application.Common.Interfaces;
using TicketPermissions = ServiceManagement.Domain.Constants.Permissions;

namespace ServiceManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Ambient caller for tests that exercise infrastructure services directly,
/// outside an HTTP request where no principal exists.
/// </summary>
public sealed class TestCurrentUser : ICurrentUserService
{
    /// <summary>
    /// The default caller is staff: most tests exercise mechanics, not permission
    /// denials, and the ticket read paths scope callers without staff permissions
    /// down to their own tickets. Visibility tests pass an explicit limited set.
    /// </summary>
    public static readonly IReadOnlyCollection<string> StaffPermissions =
    [
        TicketPermissions.TicketsRead,
        TicketPermissions.TicketsCreate,
        TicketPermissions.TicketsUpdate,
        TicketPermissions.TicketsAssign,
        TicketPermissions.TicketsComment,
        TicketPermissions.TicketsCommentInternal,
        TicketPermissions.TicketsManage,
        TicketPermissions.UsersRead,
        TicketPermissions.CatalogRead
    ];

    public Guid? UserId { get; init; }

    public string? UserName => "test";

    public string? Email => "test@servicemanagement.local";

    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlyCollection<string> Permissions { get; init; } = StaffPermissions;

    public string? IpAddress => "127.0.0.1";

    public string? CorrelationId => "test-correlation";

    public bool HasPermission(string permission) =>
        Permissions.Any(candidate => string.Equals(candidate, permission, StringComparison.OrdinalIgnoreCase));

    public Guid RequireUserId() => UserId ?? Guid.CreateVersion7();
}

/// <summary>Null logger for services that only need one to satisfy a constructor.</summary>
public static class TestLoggers
{
    public static ILogger<T> For<T>() => NullLogger<T>.Instance;
}
