using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.UnitTests.Configuration;

/// <summary>
/// The placeholder key in appsettings.json is longer than the minimum length, so
/// length validation alone would accept it and the API would sign tokens with a
/// secret published in the repository. Startup must fail instead.
/// </summary>
public class JwtSigningKeyValidatorTests
{
    private static ValidationResult? Validate(string signingKey)
    {
        var options = new JwtOptions
        {
            Issuer = "ServiceManagement.Api",
            Audience = "ServiceManagement.Clients",
            SigningKey = signingKey
        };

        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results.FirstOrDefault(result => result.MemberNames.Contains(nameof(JwtOptions.SigningKey)));
    }

    [Fact]
    public void The_placeholder_key_is_rejected()
    {
        var result = Validate(JwtOptions.PlaceholderSigningKey);

        result.Should().NotBeNull("a deployment that never replaced the key must fail at startup");
        result!.ErrorMessage.Should().Contain("placeholder");
    }

    [Fact]
    public void A_real_key_passes()
    {
        Validate("a-real-signing-key-that-is-long-enough-1234567890").Should().BeNull();
    }

    [Fact]
    public void A_key_that_differs_only_by_surrounding_whitespace_is_still_rejected()
    {
        Validate($"  {JwtOptions.PlaceholderSigningKey}  ").Should().NotBeNull();
    }

    [Fact]
    public void A_key_that_merely_contains_the_placeholder_is_allowed()
    {
        // Only the exact shipped value is rejected; real secrets may contain any
        // characters, so a substring match would create false failures.
        Validate($"{JwtOptions.PlaceholderSigningKey}-but-rotated-1234567890")
            .Should().BeNull();
    }
}
