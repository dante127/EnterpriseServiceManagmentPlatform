using System.Linq.Expressions;
using ServiceManagement.Domain.Sla;
using ServiceManagement.Domain.Tickets;

namespace ServiceManagement.Application.Tickets;

/// <summary>
/// Specification object describing a ticket query. It carries the filter and
/// ordering intent and applies it to an <see cref="IQueryable{T}"/>, which keeps
/// filter composition out of handlers and makes the supported filters explicit
/// and testable.
/// </summary>
public sealed class TicketQuerySpecification
{
    public string? TicketNumber { get; init; }

    public string? Title { get; init; }

    public TicketStatus? Status { get; init; }

    public Guid? PriorityId { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? ServiceId { get; init; }

    public Guid? RequesterId { get; init; }

    public Guid? AssignedToId { get; init; }

    public Guid? AssignedTeamId { get; init; }

    public SlaStatus? SlaStatus { get; init; }

    public DateTimeOffset? ReportedFrom { get; init; }

    public DateTimeOffset? ReportedTo { get; init; }

    public string? SortBy { get; init; }

    public bool SortDescending { get; init; } = true;

    public IQueryable<Ticket> Apply(IQueryable<Ticket> query)
    {
        // Soft-deleted tickets are invisible to every query.
        query = query.Where(ticket => !ticket.IsDeleted);

        if (!string.IsNullOrWhiteSpace(TicketNumber))
        {
            var value = TicketNumber.Trim();
            query = query.Where(ticket => ticket.TicketNumber.Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(Title))
        {
            var value = Title.Trim();
            query = query.Where(ticket => ticket.Title.Contains(value));
        }

        if (Status is not null)
        {
            query = query.Where(ticket => ticket.Status == Status);
        }

        if (PriorityId is not null)
        {
            query = query.Where(ticket => ticket.PriorityId == PriorityId);
        }

        if (CategoryId is not null)
        {
            query = query.Where(ticket => ticket.CategoryId == CategoryId);
        }

        if (ServiceId is not null)
        {
            query = query.Where(ticket => ticket.ServiceId == ServiceId);
        }

        if (RequesterId is not null)
        {
            query = query.Where(ticket => ticket.RequesterId == RequesterId);
        }

        if (AssignedToId is not null)
        {
            query = query.Where(ticket => ticket.AssignedToId == AssignedToId);
        }

        if (AssignedTeamId is not null)
        {
            query = query.Where(ticket => ticket.AssignedTeamId == AssignedTeamId);
        }

        if (SlaStatus is not null)
        {
            query = query.Where(ticket => ticket.SlaStatus == SlaStatus);
        }

        if (ReportedFrom is not null)
        {
            query = query.Where(ticket => ticket.ReportedAt >= ReportedFrom);
        }

        if (ReportedTo is not null)
        {
            query = query.Where(ticket => ticket.ReportedAt <= ReportedTo);
        }

        return ApplyOrdering(query);
    }

    private IQueryable<Ticket> ApplyOrdering(IQueryable<Ticket> query)
        => SortBy?.Trim().ToLowerInvariant() switch
        {
            "ticketnumber" => Order(query, ticket => ticket.TicketNumber, SortDescending),
            "title" => Order(query, ticket => ticket.Title, SortDescending),
            "status" => Order(query, ticket => ticket.Status, SortDescending),
            "sla" or "slastatus" => Order(query, ticket => ticket.SlaStatus, SortDescending),
            "resolutiondueat" or "duedate" => Order(query, ticket => ticket.ResolutionDueAt, SortDescending),
            "resolvedat" => Order(query, ticket => ticket.ResolvedAt, SortDescending),
            _ => Order(query, ticket => ticket.ReportedAt, SortDescending)
        };

    private static IQueryable<Ticket> Order<TKey>(
        IQueryable<Ticket> query,
        Expression<Func<Ticket, TKey>> keySelector,
        bool descending)
        => descending
            ? query.OrderByDescending(keySelector).ThenByDescending(ticket => ticket.Id)
            : query.OrderBy(keySelector).ThenBy(ticket => ticket.Id);
}
