namespace ServiceManagement.Application.Users;

/// <summary>Full projection of a user for administrative endpoints.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string FullName,
    bool IsActive,
    Guid? OrganizationId,
    Guid? DepartmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Roles);

/// <summary>Compact projection used by pickers and search results.</summary>
public sealed record UserSummaryDto(
    Guid Id,
    string FullName,
    string Email,
    bool IsActive);
