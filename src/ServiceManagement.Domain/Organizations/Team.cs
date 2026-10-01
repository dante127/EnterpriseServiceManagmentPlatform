using ServiceManagement.Domain.Common;
using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Domain.Organizations;

/// <summary>
/// A group of agents that tickets can be assigned to. Tickets are routed to a
/// team first (a queue) and only then to an individual assignee.
/// </summary>
public class Team : AuditableEntity
{
    private readonly List<TeamMember> _members = [];

    private Team()
    {
    }

    public Team(Guid organizationId, Guid departmentId, string name, string code, string? description = null)
    {
        OrganizationId = Guard.NotEmpty(organizationId, nameof(organizationId));
        DepartmentId = Guard.NotEmpty(departmentId, nameof(departmentId));
        Name = Guard.NotBlank(name, nameof(name), 200);
        Code = Guard.NotBlank(code, nameof(code), 32).ToUpperInvariant();
        Description = description?.Trim();
    }

    public Guid OrganizationId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? ManagerId { get; private set; }

    public Department Department { get; private set; } = null!;

    public User? Manager { get; private set; }

    public IReadOnlyCollection<TeamMember> Members => _members.AsReadOnly();

    public void SetManager(Guid? managerId) => ManagerId = managerId;

    public TeamMember AddMember(Guid userId, DateTimeOffset joinedAt, bool isManager = false)
    {
        var existing = _members.FirstOrDefault(m => m.UserId == userId);
        if (existing is not null)
        {
            return existing;
        }

        var member = new TeamMember(Id, userId, joinedAt, isManager);
        _members.Add(member);
        return member;
    }

    public void RemoveMember(Guid userId) => _members.RemoveAll(m => m.UserId == userId);
}
