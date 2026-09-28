using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed JWT configuration. Secrets come from environment/user secrets only.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Placeholder shipped in appsettings.json. It is deliberately not a usable
    /// key: startup must fail unless a real secret is supplied, rather than
    /// silently signing tokens with a value that is public in the repository.
    /// </summary>
    public const string PlaceholderSigningKey = "__SET_VIA_USER_SECRETS_OR_ENV__MIN_32_CHARACTERS__";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric signing key. Minimum 32 characters; never committed with a real value.</summary>
    [Required]
    [MinLength(32, ErrorMessage = "The JWT signing key must be at least 32 characters to be a valid HMAC-SHA256 key.")]
    [CustomValidation(typeof(JwtSigningKeyValidator), nameof(JwtSigningKeyValidator.Validate))]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 30;
}
