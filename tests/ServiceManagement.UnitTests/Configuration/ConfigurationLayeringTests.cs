using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.UnitTests.Configuration;

/// <summary>
/// Proves how the options binder treats a value that exists in the base
/// appsettings.json but is absent from the environment-specific overlay.
/// </summary>
public class ConfigurationLayeringTests
{
    [Fact]
    public void A_required_property_survives_an_overlay_that_omits_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("""
                { "Jwt": { "Issuer": "Base.Issuer", "Audience": "Base.Audience" } }
                """u8.ToArray()))
            .AddJsonStream(new MemoryStream("""
                { "Jwt": { "AccessTokenMinutes": 45 } }
                """u8.ToArray()))
            .Build();

        var jwt = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwt);

        jwt.Issuer.Should().Be("Base.Issuer", "the overlay must not blank a value the base file provides");
        jwt.AccessTokenMinutes.Should().Be(45);
    }

    [Fact]
    public void A_comment_key_inside_a_binds_to_section_is_harmless()
    {
        // The shipped files annotate sections with a "//" key. This guards against
        // a future assumption that such a key interferes with binding.
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("""
                { "Jwt": { "Issuer": "Base.Issuer", "Audience": "Base.Audience" } }
                """u8.ToArray()))
            .AddJsonStream(new MemoryStream("""
                { "Jwt": { "//": "an explanatory note", "AccessTokenMinutes": 45 } }
                """u8.ToArray()))
            .Build();

        var jwt = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwt);

        jwt.Issuer.Should().Be("Base.Issuer");
        jwt.AccessTokenMinutes.Should().Be(45);
    }

    [Fact]
    public void A_required_string_with_the_default_initializer_fails_when_the_key_is_absent_everywhere()
    {
        var configuration = new ConfigurationBuilder().Build();

        var jwt = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwt);

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(jwt, new ValidationContext(jwt), results, validateAllProperties: true);

        results.Select(result => result.MemberNames)
            .Should().Contain(names => names.Contains(nameof(JwtOptions.Issuer)));
    }
}
