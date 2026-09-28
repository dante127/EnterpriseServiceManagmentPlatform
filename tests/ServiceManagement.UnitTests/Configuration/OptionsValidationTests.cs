using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Options;
using ServiceManagement.Infrastructure;

namespace ServiceManagement.UnitTests.Configuration;

/// <summary>
/// Options are validated at startup so a misconfigured deployment fails on boot
/// instead of on the first request that happens to touch the broken setting.
/// </summary>
public class OptionsValidationTests
{
    [Fact]
    public void A_complete_configuration_binds_and_validates()
    {
        using var provider = BuildServices<JwtOptions>(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ServiceManagement.Api",
                ["Jwt:Audience"] = "ServiceManagement.Clients",
                ["Jwt:SigningKey"] = "a-signing-key-that-is-long-enough-1234567890",
                ["Jwt:AccessTokenMinutes"] = "45"
            },
            JwtOptions.SectionName);

        var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        jwt.Issuer.Should().Be("ServiceManagement.Api");
        jwt.AccessTokenMinutes.Should().Be(45);
    }

    [Fact]
    public void A_short_signing_key_is_rejected()
    {
        // Without validation this key reaches the token validator and only fails
        // once a real request arrives, long after the host reported itself healthy.
        using var provider = BuildServices<JwtOptions>(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ServiceManagement.Api",
                ["Jwt:Audience"] = "ServiceManagement.Clients",
                ["Jwt:SigningKey"] = "too-short"
            },
            JwtOptions.SectionName);

        var act = () => provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void A_missing_signing_key_is_rejected()
    {
        using var provider = BuildServices<JwtOptions>(
            new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "ServiceManagement.Api",
                ["Jwt:Audience"] = "ServiceManagement.Clients"
            },
            JwtOptions.SectionName);

        var act = () => provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void An_out_of_range_sla_threshold_is_rejected(int threshold)
    {
        using var provider = BuildServices<SlaOptions>(
            new Dictionary<string, string?>
            {
                ["Sla:AtRiskThresholdPercent"] = threshold.ToString()
            },
            SlaOptions.SectionName);

        var act = () => provider.GetRequiredService<IOptions<SlaOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void A_negative_command_timeout_is_rejected()
    {
        using var provider = BuildServices<DatabaseOptions>(
            new Dictionary<string, string?>
            {
                ["Database:CommandTimeoutSeconds"] = "-5"
            },
            DatabaseOptions.SectionName);

        var act = () => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    /// <summary>Registers one options class exactly as the composition root does.</summary>
    private static ServiceProvider BuildServices<TOptions>(
        Dictionary<string, string?> settings,
        string sectionName)
        where TOptions : class
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();

        // Only the option binding is exercised; the full host needs SQL Server.
        services.AddValidatedOptions<TOptions>(configuration, sectionName);

        return services.BuildServiceProvider();
    }
}
