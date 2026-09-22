namespace ServiceManagement.Domain.Constants;

/// <summary>
/// Canonical permission names. Authorization is permission based: controllers
/// never branch on role names.
/// </summary>
public static class Permissions
{
    public const string TicketsRead = "tickets.read";
    public const string TicketsCreate = "tickets.create";
    public const string TicketsUpdate = "tickets.update";
    public const string TicketsAssign = "tickets.assign";
    public const string TicketsDelete = "tickets.delete";
    public const string TicketsComment = "tickets.comment";
    public const string TicketsCommentInternal = "tickets.comment.internal";
    public const string TicketsManage = "tickets.manage";

    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";

    public const string TeamsManage = "teams.manage";

    public const string CatalogRead = "catalog.read";
    public const string CatalogManage = "catalog.manage";

    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audit.read";
    public const string NotificationsRead = "notifications.read";

    /// <summary>Prefix used when a permission is turned into an ASP.NET Core authorization policy.</summary>
    public const string PolicyPrefix = "permission:";

    public static string Policy(string permission) => PolicyPrefix + permission;

    public static readonly IReadOnlyList<string> All =
    [
        TicketsRead, TicketsCreate, TicketsUpdate, TicketsAssign, TicketsDelete,
        TicketsComment, TicketsCommentInternal, TicketsManage,
        UsersRead, UsersManage, TeamsManage,
        CatalogRead, CatalogManage,
        ReportsRead, AuditRead, NotificationsRead
    ];
}
