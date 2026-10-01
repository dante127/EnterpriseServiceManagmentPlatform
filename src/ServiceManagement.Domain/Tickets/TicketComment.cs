using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Domain.Tickets;

/// <summary>
/// A comment on a ticket. Internal comments are only visible to agents holding
/// the <c>tickets.comment.internal</c> permission.
/// </summary>
public class TicketComment : AuditableEntity
{
    private TicketComment()
    {
    }

    public TicketComment(Guid ticketId, Guid authorId, string content, bool isInternal, DateTimeOffset createdAt)
    {
        TicketId = Guard.NotEmpty(ticketId, nameof(ticketId));
        AuthorId = Guard.NotEmpty(authorId, nameof(authorId));
        Content = Guard.NotBlank(content, nameof(content), 4000);
        IsInternal = isInternal;
        CreatedAt = createdAt;
    }

    public Guid TicketId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public bool IsInternal { get; private set; }

    public Ticket Ticket { get; private set; } = null!;

    public User Author { get; private set; } = null!;
}
