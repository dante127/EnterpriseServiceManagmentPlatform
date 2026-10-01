using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.Api.Configuration;

/// <summary>
/// Refuses to start a production deployment on the development signing key.
/// <para>
/// The dev key ships in appsettings.Development.json and .env.example, so it is
/// public in the repository: a production stack booted with it issues tokens
/// that anyone with repository access can forge for any permission. The
/// DataAnnotations validator in Application cannot know the environment, so the
/// environment-aware rule lives here in the composition root and only bites
/// where it matters.
/// </para>
/// </summary>
public sealed class ProductionSigningKeyValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    /// <summary>The key shipped in appsettings.Development.json and .env.example.</summary>
    public const string ShippedDevelopmentKey = "dev-only-signing-key-please-replace-in-production-1234567890";

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (!environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        if (options.SigningKey.Length == 0)
        {
            // Nothing to compare: [Required] and the custom validator own that failure.
            return ValidateOptionsResult.Success;
        }

        if (string.Equals(options.SigningKey.Trim(), ShippedDevelopmentKey, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey is still the development key published in this repository. Production refuses to "
                + "start with a forgeable secret: supply a real random value via Jwt__SigningKey, for example with "
                + "`openssl rand -base64 48`.");
        }

        return ValidateOptionsResult.Success;
    }
}
