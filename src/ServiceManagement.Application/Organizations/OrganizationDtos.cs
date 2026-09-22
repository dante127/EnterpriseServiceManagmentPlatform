namespace ServiceManagement.Application.Organizations;

/// <summary>Organization projection.</summary>
public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Code,
    string? Description,
    bool IsActive,
    int DepartmentCount,
    int TeamCount);

/// <summary>Department projection.</summary>
public sealed record DepartmentDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Code,
    string? Description);

/// <summary>Team projection, including membership size and manager.</summary>
public sealed record TeamDto(
    Guid Id,
    Guid OrganizationId,
    Guid DepartmentId,
    string Name,
    string Code,
    string? Description,
    Guid? ManagerId,
    string? ManagerName,
    int MemberCount);

/// <summary>Team membership projection.</summary>
public sealed record TeamMemberDto(
    Guid UserId,
    string FullName,
    string Email,
    bool IsManager,
    DateTimeOffset JoinedAt);
