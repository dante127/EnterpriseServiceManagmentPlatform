using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.UnitTests.Configuration;

/// <summary>
/// The shipped appsettings files must not contradict each other.
/// <para>
/// Configuration layers merge, but a <c>[Required]</c> string is only satisfied
/// when a value is present in the composed result. An environment-specific file
/// that omits a required key does not fall back to the base file for that
/// property unless the base file is also loaded, so a Production override that
/// redefines a section must repeat everything that section requires.
/// </para>
/// </summary>
public class ShippedConfigurationTests
{
    private static readonly string ApiSettingsDirectory = ResolveApiSettingsDirectory();

    [Fact]
    public void The_production_file_does_not_shadow_required_jwt_values_with_nulls()
    {
        var production = LoadSettings("appsettings.Production.json");
        var baseline = LoadSettings("appsettings.json");

        // A null here would override the inherited value and fail validation.
        foreach (var property in production["Jwt"]!.AsObject().Select(entry => entry.Key))
        {
            if (property.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            production["Jwt"]![property]!.GetValueKind().Should().NotBe(
                JsonValueKind.Null,
                $"Jwt:{property} must not be explicitly null in the Production overlay");
        }

        // Issuer, Audience and the rest of the base values must stay reachable:
        // the overlay may omit them, but redefining the section must not blank them.
        foreach (var property in baseline["Jwt"]!.AsObject().Select(entry => entry.Key))
        {
            if (property.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // SigningKey is supplied per environment, so its absence is intended.
            if (property == nameof(JwtOptions.SigningKey))
            {
                continue;
            }

            var value = production["Jwt"]![property];

            (value is null || value.GetValueKind() != JsonValueKind.Null)
                .Should().BeTrue($"Jwt:{property} must not be blanked by the Production overlay");
        }
    }

    [Fact]
    public void The_production_file_never_carries_a_connection_string()
    {
        var production = LoadSettings("appsettings.Production.json");

        production["ConnectionStrings"]!.AsObject()
            .Select(entry => entry.Key)
            .Should().NotContain("Default", "a credential must never be committed to a settings file");
    }

    [Fact]
    public void The_shipped_placeholder_key_is_the_one_the_validator_rejects()
    {
        // If these drift, the placeholder stops being detected and a deployment
        // would silently start signing tokens with a committed secret.
        var baseline = LoadSettings("appsettings.json");
        var shipped = baseline["Jwt"]!["SigningKey"]!.GetValue<string>();

        shipped.Should().Be(JwtOptions.PlaceholderSigningKey);
    }

    [Fact]
    public void The_production_file_does_not_enable_sensitive_data_logging()
    {
        var production = LoadSettings("appsettings.Production.json");

        production["Database"]!["EnableSensitiveDataLogging"]!.GetValue<bool>().Should().BeFalse();
    }

    private static JsonNode LoadSettings(string fileName)
    {
        var path = Path.Combine(ApiSettingsDirectory, fileName);

        File.Exists(path).Should().BeTrue("expected the shipped settings file at {0}", path);

        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    /// <summary>
    /// Walks up from the test binaries to the repository root, then into the API
    /// project, so the test asserts on the files that actually ship.
    /// </summary>
    private static string ResolveApiSettingsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "ServiceManagement.Api");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate src/ServiceManagement.Api from " + AppContext.BaseDirectory);
    }
}
