namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed JWT configuration. Secrets come from environment/user secrets only.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric signing key. Minimum 32 characters; never committed with a real value.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;
}
