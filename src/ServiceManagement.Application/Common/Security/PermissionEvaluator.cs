using ServiceManagement.Domain.Constants;

namespace ServiceManagement.Application.Common.Security;

/// <summary>
/// Pure permission logic, extracted so it can be unit tested without HTTP or
/// EF Core. The authorization handler uses the same rules.
/// </summary>
public static class PermissionEvaluator
{
    public static bool HasPermission(IEnumerable<string> granted, string required)
    {
        ArgumentNullException.ThrowIfNull(granted);
        ArgumentException.ThrowIfNullOrWhiteSpace(required);

        return granted.Any(permission =>
            string.Equals(permission, required, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasAllPermissions(IEnumerable<string> granted, IEnumerable<string> required)
    {
        var grantedSet = granted as IReadOnlyCollection<string> ?? granted.ToList();
        return required.All(permission => HasPermission(grantedSet, permission));
    }
}

/// <summary>
/// Ticket visibility rules: internal comments are agent-only, and a closed or
/// cancelled ticket is read-only.
/// </summary>
public static class TicketAccessPolicy
{
    public static bool CanViewInternalComments(IReadOnlyCollection<string> permissions)
        => PermissionEvaluator.HasPermission(permissions, Permissions.TicketsCommentInternal);

    public static bool CanCreateInternalComment(bool isInternal, IReadOnlyCollection<string> permissions)
        => !isInternal || CanViewInternalComments(permissions);

    public static bool CanModifyTicket(Domain.Tickets.TicketStatus status)
        => !Domain.Tickets.TicketWorkflow.IsTerminal(status);

    /// <summary>
    /// Staff visibility: the whole queue. Callers without one of these permissions
    /// - typically requesters - see only the tickets they raised, in every ticket
    /// read path (search, detail, history).
    /// </summary>
    public static bool CanViewAllTickets(IReadOnlyCollection<string> permissions)
        => PermissionEvaluator.HasPermission(permissions, Permissions.TicketsUpdate)
        || PermissionEvaluator.HasPermission(permissions, Permissions.TicketsAssign)
        || PermissionEvaluator.HasPermission(permissions, Permissions.TicketsManage);
}
