using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Reusable server-side projections for tickets. Projection happens in SQL, so
/// only the columns the endpoint actually needs are selected and no EF entity
/// ever crosses the API boundary.
/// </summary>
internal static class TicketProjection
{
    public static IQueryable<TicketDetailDto> ToDetailDto(this IQueryable<Ticket> query)
        => query.Select(ticket => new TicketDetailDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Title,
            ticket.Description,
            ticket.ServiceId,
            ticket.Service.Name,
            ticket.CategoryId,
            ticket.Category.Name,
            ticket.PriorityId,
            ticket.Priority.Name,
            ticket.Status.ToString(),
            ticket.RequesterId,
            ticket.Requester.FirstName + " " + ticket.Requester.LastName,
            ticket.AssignedToId,
            ticket.AssignedTo == null
                ? null
                : ticket.AssignedTo.FirstName + " " + ticket.AssignedTo.LastName,
            ticket.AssignedTeamId,
            ticket.AssignedTeam == null ? null : ticket.AssignedTeam.Name,
            ticket.ReportedAt,
            ticket.FirstRespondedAt,
            ticket.FirstResponseDueAt,
            ticket.ResolutionDueAt,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            ticket.SlaStatus,
            ticket.RowVersion));

    public static IQueryable<TicketSummaryDto> ToSummaryDto(this IQueryable<Ticket> query)
        => query.Select(ticket => new TicketSummaryDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Title,
            ticket.Status.ToString(),
            ticket.Priority.Name,
            ticket.Service.Name,
            ticket.Requester.FirstName + " " + ticket.Requester.LastName,
            ticket.AssignedTo == null
                ? null
                : ticket.AssignedTo.FirstName + " " + ticket.AssignedTo.LastName,
            ticket.SlaStatus,
            ticket.ReportedAt,
            ticket.ResolutionDueAt,
            ticket.ResolvedAt,
            ticket.Comments.Count));
}
