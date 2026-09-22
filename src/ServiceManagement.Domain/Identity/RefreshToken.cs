using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Identity;

/// <summary>
/// A refresh token. Only the SHA-256 hash of the token is persisted, so a
/// database leak cannot be replayed against the API. Rotation is tracked with
/// <see cref="ReplacedByTokenHash"/>.
/// </summary>
public class RefreshToken : BaseEntity
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string? createdByIp)
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        TokenHash = Guard.NotBlank(tokenHash, nameof(tokenHash), 512);
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public string? CreatedByIp { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedByIp { get; private set; }

    public string? ReplacedByTokenHash { get; private set; }

    public string? ReasonRevoked { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && !IsExpired(now);

    public void Revoke(
        DateTimeOffset at,
        string? revokedByIp,
        string reason,
        string? replacedByTokenHash = null)
    {
        RevokedAt = at;
        RevokedByIp = revokedByIp;
        ReasonRevoked = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
