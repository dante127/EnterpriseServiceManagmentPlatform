using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Exceptions;
using ServiceManagement.Domain.Identity;
using ServiceManagement.Domain.Organizations;
using ServiceManagement.Domain.ServiceCatalog;
using ServiceManagement.Domain.Sla;

namespace ServiceManagement.Domain.Tickets;

/// <summary>
/// The ticket aggregate root. Ticket numbers are generated once and never
/// change; SLA deadlines are copied from the active policy at creation time and
/// recalculated whenever the priority changes.
/// </summary>
public class Ticket : SoftDeletableEntity
{
    private readonly List<TicketComment> _comments = [];
    private readonly List<TicketAssignmentHistory> _assignmentHistory = [];

    private Ticket()
    {
    }

    public Ticket(
        string ticketNumber,
        string title,
        string description,
        Guid serviceId,
        Guid categoryId,
        Guid priorityId,
        Guid requesterId,
        DateTimeOffset reportedAt)
    {
        TicketNumber = Guard.NotBlank(ticketNumber, nameof(ticketNumber), 32);
        Title = Guard.NotBlank(title, nameof(title), 300);
        Description = Guard.NotBlank(description, nameof(description), 8000);
        ServiceId = Guard.NotEmpty(serviceId, nameof(serviceId));
        CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));
        PriorityId = Guard.NotEmpty(priorityId, nameof(priorityId));
        RequesterId = Guard.NotEmpty(requesterId, nameof(requesterId));
        ReportedAt = reportedAt;
        Status = TicketStatus.New;
        SlaStatus = SlaStatus.WithinSla;
    }

    public string TicketNumber { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Guid ServiceId { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid PriorityId { get; private set; }

    public TicketStatus Status { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid? AssignedToId { get; private set; }

    public Guid? AssignedTeamId { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }

    public DateTimeOffset? FirstRespondedAt { get; private set; }

    public DateTimeOffset? FirstResponseDueAt { get; private set; }

    public DateTimeOffset? ResolutionDueAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public SlaStatus SlaStatus { get; private set; }

    /// <summary>SQL Server rowversion: drives optimistic concurrency (HTTP 409 on conflict).</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Optional client-supplied idempotency key. A repeated create with the same
    /// key returns the ticket created by the first request instead of duplicating it.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public Service Service { get; private set; } = null!;

    public ServiceCategory Category { get; private set; } = null!;

    public ServicePriority Priority { get; private set; } = null!;

    public User Requester { get; private set; } = null!;

    public User? AssignedTo { get; private set; }

    public Team? AssignedTeam { get; private set; }

    public IReadOnlyCollection<TicketComment> Comments => _comments.AsReadOnly();

    public IReadOnlyCollection<TicketAssignmentHistory> AssignmentHistory => _assignmentHistory.AsReadOnly();

    public void UpdateDetails(string title, string description, Guid categoryId, Guid priorityId)
    {
        EnsureNotTerminal();
        Title = Guard.NotBlank(title, nameof(title), 300);
        Description = Guard.NotBlank(description, nameof(description), 8000);
        CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));

        // Only a genuine priority change invalidates the SLA deadlines. Calling
        // ChangePriority unconditionally wiped them on every ordinary edit, which
        // made the ticket permanently invisible to the SLA monitor.
        if (priorityId != PriorityId)
        {
            ChangePriority(priorityId);
        }
    }

    public void ChangePriority(Guid priorityId)
    {
        EnsureNotTerminal();
        PriorityId = Guard.NotEmpty(priorityId, nameof(priorityId));

        // Deadline and state are derived values: they must be recomputed, not left stale.
        FirstResponseDueAt = null;
        ResolutionDueAt = null;
    }

    /// <summary>Applies an SLA policy (from the database) to this ticket.</summary>
    public void ApplySlaPolicy(
        int firstResponseMinutes,
        int resolutionMinutes,
        int atRiskThresholdPercent)
    {
        var deadlines = SlaCalculator.CalculateDeadlines(ReportedAt, firstResponseMinutes, resolutionMinutes);
        FirstResponseDueAt = deadlines.FirstResponseDueAt;
        ResolutionDueAt = deadlines.ResolutionDueAt;
        RefreshSlaStatus(ReportedAt, atRiskThresholdPercent);
    }

    public void RefreshSlaStatus(DateTimeOffset now, int atRiskThresholdPercent)
    {
        if (FirstResponseDueAt is null || ResolutionDueAt is null)
        {
            SlaStatus = ResolvedAt is not null ? SlaStatus.Completed : SlaStatus.WithinSla;
            return;
        }

        SlaStatus = SlaCalculator.Evaluate(
            now,
            ReportedAt,
            FirstResponseDueAt.Value,
            ResolutionDueAt.Value,
            FirstRespondedAt,
            ResolvedAt,
            atRiskThresholdPercent);
    }

    public void MarkFirstResponse(DateTimeOffset at) => FirstRespondedAt ??= at;

    public void SetIdempotencyKey(string? idempotencyKey)
        => IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

    /// <summary>
    /// Assigns a user and returns the new trail entry so the caller can persist it.
    /// <para>
    /// The entry is returned rather than exposed as a property on purpose: a
    /// navigable <c>TicketAssignmentHistory</c> member makes EF infer a second,
    /// shadow-keyed relationship back to <c>Ticket</c>, which silently changes the
    /// mapped model.
    /// </para>
    /// </summary>
    public TicketAssignmentHistory AssignToUser(Guid assigneeId, Guid changedBy, string? reason, DateTimeOffset at)
    {
        Guard.NotEmpty(assigneeId, nameof(assigneeId));
        EnsureNotTerminal();

        var previousAssignee = AssignedToId;
        var previousTeam = AssignedTeamId;

        AssignedToId = assigneeId;
        var entry = RecordAssignment(previousAssignee, assigneeId, previousTeam, AssignedTeamId, reason, changedBy, at);

        if (Status == TicketStatus.New)
        {
            ChangeStatus(TicketStatus.Assigned, changedBy, at);
        }

        return entry;
    }

    /// <summary>Assigns a team and returns the new trail entry so the caller can persist it.</summary>
    public TicketAssignmentHistory AssignToTeam(Guid teamId, Guid changedBy, string? reason, DateTimeOffset at)
    {
        Guard.NotEmpty(teamId, nameof(teamId));
        EnsureNotTerminal();

        var previousAssignee = AssignedToId;
        var previousTeam = AssignedTeamId;

        AssignedTeamId = teamId;
        var entry = RecordAssignment(previousAssignee, AssignedToId, previousTeam, teamId, reason, changedBy, at);

        if (Status == TicketStatus.New)
        {
            ChangeStatus(TicketStatus.Assigned, changedBy, at);
        }

        return entry;
    }

    public void ChangeStatus(TicketStatus next, Guid changedBy, DateTimeOffset at)
    {
        if (next == Status)
        {
            throw new DomainRuleViolationException($"Ticket is already in status '{next}'.");
        }

        TicketWorkflow.EnsureTransition(Status, next);

        var isFirstResponseCandidate = Status is TicketStatus.Assigned or TicketStatus.New
            && next == TicketStatus.InProgress;

        Status = next;

        if (isFirstResponseCandidate)
        {
            MarkFirstResponse(at);
        }

        switch (next)
        {
            case TicketStatus.Resolved:
                ResolvedAt = at;
                MarkFirstResponse(at);
                break;
            case TicketStatus.Closed:
                ClosedAt = at;
                if (ResolvedAt is null)
                {
                    ResolvedAt = at;
                }

                break;
            case TicketStatus.InProgress:
                ResolvedAt = null;
                ClosedAt = null;
                break;
            case TicketStatus.Cancelled:
                break;
        }
    }

    public TicketComment AddComment(string content, Guid authorId, bool isInternal, DateTimeOffset at)
    {
        var comment = new TicketComment(Id, authorId, content, isInternal, at);
        _comments.Add(comment);
        return comment;
    }

    private TicketAssignmentHistory RecordAssignment(
        Guid? previousAssignee,
        Guid? newAssignee,
        Guid? previousTeam,
        Guid? newTeam,
        string? reason,
        Guid changedBy,
        DateTimeOffset at)
    {
        var entry = new TicketAssignmentHistory(
            Id,
            previousAssignee,
            newAssignee,
            previousTeam,
            newTeam,
            reason,
            changedBy,
            at);

        _assignmentHistory.Add(entry);

        return entry;
    }

    private void EnsureNotTerminal()
    {
        if (TicketWorkflow.IsTerminal(Status))
        {
            throw new DomainRuleViolationException(
                $"Ticket in terminal status '{Status}' can no longer be modified.");
        }
    }
}
