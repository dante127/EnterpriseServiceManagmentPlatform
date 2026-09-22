using ServiceManagement.Domain.Sla;

namespace ServiceManagement.Application.Tickets;

/// <summary>Full ticket projection returned by single-resource endpoints.</summary>
public sealed record TicketDetailDto(
    Guid Id,
    string TicketNumber,
    string Title,
    string Description,
    Guid ServiceId,
    string ServiceName,
    Guid CategoryId,
    string CategoryName,
    Guid PriorityId,
    string PriorityName,
    string Status,
    Guid RequesterId,
    string RequesterName,
    Guid? AssignedToId,
    string? AssignedToName,
    Guid? AssignedTeamId,
    string? AssignedTeamName,
    DateTimeOffset ReportedAt,
    DateTimeOffset? FirstRespondedAt,
    DateTimeOffset? FirstResponseDueAt,
    DateTimeOffset? ResolutionDueAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    SlaStatus SlaStatus,
    byte[] RowVersion);

/// <summary>Compact ticket projection used by list/search endpoints.</summary>
public sealed record TicketSummaryDto(
    Guid Id,
    string TicketNumber,
    string Title,
    string Status,
    string PriorityName,
    string ServiceName,
    string RequesterName,
    string? AssignedToName,
    SlaStatus SlaStatus,
    DateTimeOffset ReportedAt,
    DateTimeOffset? ResolutionDueAt,
    DateTimeOffset? ResolvedAt,
    int CommentCount);

/// <summary>Ticket comment projection. Internal comments are filtered before projection.</summary>
public sealed record TicketCommentDto(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    bool IsInternal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Assignment history projection.</summary>
public sealed record TicketAssignmentHistoryDto(
    Guid Id,
    Guid? PreviousAssigneeId,
    string? PreviousAssigneeName,
    Guid? NewAssigneeId,
    string? NewAssigneeName,
    Guid? PreviousTeamId,
    Guid? NewTeamId,
    string? Reason,
    Guid ChangedBy,
    string ChangedByName,
    DateTimeOffset ChangedAt);

/// <summary>Comment plus assignment trail of a ticket.</summary>
public sealed record TicketHistoryDto(
    Guid TicketId,
    IReadOnlyList<TicketCommentDto> Comments,
    IReadOnlyList<TicketAssignmentHistoryDto> Assignments);
