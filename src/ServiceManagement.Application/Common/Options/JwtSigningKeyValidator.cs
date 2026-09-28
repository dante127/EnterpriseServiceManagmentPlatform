using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>
/// Rejects the placeholder signing key that ships in appsettings.json.
/// <para>
/// The placeholder is longer than the 32-character minimum, so length validation
/// alone would let it through and the API would quietly sign tokens with a secret
/// published in the repository. Requiring a replacement makes a misconfigured
/// deployment fail at startup instead of issuing forgeable tokens.
/// </para>
/// </summary>
public static class JwtSigningKeyValidator
{
    public static ValidationResult? Validate(object? value, ValidationContext context)
    {
        var signingKey = value as string;

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            return ValidationResult.Success;
        }

        if (string.Equals(signingKey.Trim(), JwtOptions.PlaceholderSigningKey, StringComparison.Ordinal))
        {
            return new ValidationResult(
                "Jwt:SigningKey is still the placeholder from appsettings.json. Supply a real secret via "
                + "Jwt__SigningKey (environment variable) or user secrets, for example with `openssl rand -base64 48`.",
                [context.MemberName ?? nameof(JwtOptions.SigningKey)]);
        }

        return ValidationResult.Success;
    }
}
