using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ServiceManagement.Application.Common.Interfaces;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Domain.Identity;

using ServiceManagement.Application.Common.Security;

namespace ServiceManagement.Infrastructure.Security;

/// <summary>
/// Issues short-lived, signed access tokens and opaque refresh tokens.
/// Refresh tokens are handed to the client once and stored only as a SHA-256
/// hash, so the database never contains a usable credential.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private const int MinimumSigningKeyLength = 32;

    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public AccessTokenResult CreateAccessToken(
        User user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var signingKey = ResolveSigningKey();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(PermissionClaims.SecurityStamp, user.SecurityStamp)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim(PermissionClaims.Permission, permission)));

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.AccessTokenMinutes));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(signingKey),
                SecurityAlgorithms.HmacSha256));

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string CreateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    private byte[] ResolveSigningKey()
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKey) ||
            _options.SigningKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be configured with at least {MinimumSigningKeyLength} characters. " +
                "Provide it through environment variables or user secrets - never in source control.");
        }

        return Encoding.UTF8.GetBytes(_options.SigningKey);
    }
}
