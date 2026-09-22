namespace ServiceManagement.Application.Auth;

/// <summary>Issued credential pair returned by login and refresh.</summary>
public sealed record AuthTokenDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserSessionDto User);

/// <summary>Minimal identity information for the authenticated caller.</summary>
public sealed record UserSessionDto(
    Guid Id,
    string Email,
    string UserName,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
