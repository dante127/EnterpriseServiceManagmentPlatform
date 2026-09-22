using ServiceManagement.Domain.Identity;

namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>A freshly minted access token and its expiry.</summary>
public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues and validates the credentials used by the API.</summary>
public interface IJwtTokenService
{
    AccessTokenResult CreateAccessToken(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions);

    /// <summary>Creates an opaque, cryptographically random refresh token.</summary>
    string CreateRefreshToken();

    /// <summary>Hashes a refresh token for storage/lookup (only hashes are persisted).</summary>
    string HashRefreshToken(string refreshToken);
}

/// <summary>Password hashing abstraction (ASP.NET Core Identity PBKDF2 in production).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
