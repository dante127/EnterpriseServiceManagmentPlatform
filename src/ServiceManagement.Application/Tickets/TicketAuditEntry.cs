using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Builds an audit entry for a ticket change, filling in the ambient caller
/// details so no handler has to repeat them.
/// <para>
/// Three arguments of every audit record are always the same - the acting user,
/// their IP and the correlation id - and they come from the caller rather than
/// from the thing being changed. Passing them explicitly at every call site
/// invited the classic mistake of omitting one, which would have quietly
/// attributed a change to nobody.
/// </para>
/// </summary>
internal static class TicketAuditEntry
{
    /// <param name="userId">
    /// Overrides the ambient user. The assignment, status and comment handlers
    /// pass the id they already resolved through <c>RequireUserId</c>, which is
    /// what proved the request was authenticated.
    /// </param>
    public static AuditEntry For(
        string action,
        string entityName,
        string entityId,
        ICurrentUserService currentUser,
        string? oldValues = null,
        string? newValues = null,
        Guid? userId = null)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return new AuditEntry(
            action,
            entityName,
            entityId,
            userId ?? currentUser.UserId,
            currentUser.IpAddress,
            currentUser.CorrelationId,
            oldValues,
            newValues);
    }
}
