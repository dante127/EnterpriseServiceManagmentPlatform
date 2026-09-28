using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.UnitTests.Configuration;

/// <summary>
/// A published application resolves its settings from the content root, not from
/// the directory that happens to contain the assembly.
/// <para>
/// Running <c>dotnet /path/to/ServiceManagement.Api.dll</c> from an unrelated
/// working directory silently produces an empty configuration: every
/// <c>[Required]</c> option then fails at startup with a message that points at
/// the option rather than at the missing file. Asserting on the real file layout
/// keeps that failure mode visible.
/// </para>
/// </summary>
public class PublishedContentRootTests
{
    [Fact]
    public void Every_shipped_settings_file_is_copied_to_the_output_directory()
    {
        var sourceDirectory = ResolveApiProjectDirectory();
        var outputDirectory = Path.Combine(sourceDirectory, "bin", "Debug", "net10.0");

        foreach (var fileName in new[]
                 {
                     "appsettings.json",
                     "appsettings.Development.json",
                     "appsettings.Production.json"
                 })
        {
            File.Exists(Path.Combine(sourceDirectory, fileName))
                .Should().BeTrue("expected {0} in the API project", fileName);

            File.Exists(Path.Combine(outputDirectory, fileName))
                .Should().BeTrue(
                    "{0} must be copied to the output directory, otherwise a host launched from the "
                    + "published folder cannot find it",
                    fileName);
        }
    }

    [Fact]
    public void The_production_overlay_is_validated_by_the_same_rules_as_the_base_file()
    {
        // Loading the two real files through the configuration builder reproduces
        // what the host does, so a Production overlay that blanks a required value
        // fails here rather than on a deployed machine.
        var apiProject = ResolveApiProjectDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProject)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var jwt = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwt);

        jwt.Issuer.Should().NotBeNullOrWhiteSpace();
        jwt.Audience.Should().NotBeNullOrWhiteSpace();
    }

    private static string ResolveApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "ServiceManagement.Api");

            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate src/ServiceManagement.Api from " + AppContext.BaseDirectory);
    }
}
