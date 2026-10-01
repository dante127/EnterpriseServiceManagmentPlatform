using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Domain.Organizations;

/// <summary>Membership of a user in a team, optionally flagged as team manager.</summary>
public class TeamMember : BaseEntity
{
    private TeamMember()
    {
    }

    public TeamMember(Guid teamId, Guid userId, DateTimeOffset joinedAt, bool isManager = false)
    {
        TeamId = Guard.NotEmpty(teamId, nameof(teamId));
        UserId = Guard.NotEmpty(userId, nameof(userId));
        IsManager = isManager;
        JoinedAt = joinedAt;
    }

    public Guid TeamId { get; private set; }

    public Guid UserId { get; private set; }

    public bool IsManager { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public Team Team { get; private set; } = null!;

    public User User { get; private set; } = null!;
}
