using Microsoft.AspNetCore.Identity;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.Infrastructure.Security;

/// <summary>
/// Wraps ASP.NET Core Identity's PBKDF2 implementation (HMAC-SHA256, 100k+
/// iterations, per-password salt). No custom crypto is written here on purpose.
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new();

    private static readonly object HashSubject = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _inner.HashPassword(HashSubject, password);
    }

    public bool Verify(string passwordHash, string password)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var result = _inner.VerifyHashedPassword(HashSubject, passwordHash, password);

        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
