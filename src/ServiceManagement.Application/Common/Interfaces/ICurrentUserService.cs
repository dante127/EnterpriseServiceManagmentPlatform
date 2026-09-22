namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>Ambient information about the caller derived from the validated JWT.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }

    string? UserName { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    string? IpAddress { get; }

    string? CorrelationId { get; }

    bool HasPermission(string permission);

    /// <summary>Returns the caller id or throws when the request is anonymous.</summary>
    Guid RequireUserId();
}
