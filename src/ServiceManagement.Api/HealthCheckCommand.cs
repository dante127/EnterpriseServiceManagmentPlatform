using System.Net.Http.Json;

namespace ServiceManagement.Api;

/// <summary>
/// Self-contained health probe invoked as <c>dotnet ServiceManagement.Api.dll --health-check</c>.
/// <para>
/// The runtime container image carries neither curl nor wget, so the probe has to
/// come from the application itself. It resolves the probe URL from
/// <c>ASPNETCORE_URLS</c> (defaulting to the port the image exposes) and exits
/// non-zero when the API does not answer, which is what a container orchestrator
/// needs in order to restart or remove an unhealthy replica.
/// </para>
/// </summary>
internal static class HealthCheckCommand
{
    private const string ProbeArgument = "--health-probe";
    private const string DefaultProbePath = "/health/live";
    private const int TimeoutSeconds = 5;

    public static async Task<int> RunAsync(string[] args)
    {
        var probePath = ReadProbePath(args) ?? DefaultProbePath;
        var url = BuildProbeUrl(probePath);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

        try
        {
            using var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            Console.Error.WriteLine($"unhealthy: {url} returned {(int)response.StatusCode}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"unhealthy: {url} could not be reached ({exception.GetType().Name}).");
            return 1;
        }
    }

    private static string? ReadProbePath(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == ProbeArgument)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// Turns <c>ASPNETCORE_URLS</c> into a probe URL. Multiple bindings are
    /// supported and the first http/https entry wins.
    /// <para>
    /// The authority is parsed by hand rather than through <see cref="Uri"/>:
    /// <c>http://+:8080</c> is a valid Kestrel binding but not a parseable URI,
    /// so <c>Uri.TryCreate</c> rejects it. A wildcard host is replaced with the
    /// loopback address, because binding to <c>0.0.0.0</c> listens on every
    /// interface but is not a connectable destination on all platforms.
    /// </para>
    /// </summary>
    private static string BuildProbeUrl(string probePath)
    {
        const string loopback = "127.0.0.1";

        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

        if (string.IsNullOrWhiteSpace(urls))
        {
            return $"http://{loopback}:8080{probePath}";
        }

        var candidate = urls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        if (candidate is null)
        {
            return $"http://{loopback}:8080{probePath}";
        }

        var isHttps = candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var authority = candidate[(candidate.IndexOf("://", StringComparison.Ordinal) + 3)..];

        // Drop any path, query or fragment: only the host and port matter.
        var end = authority.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            authority = authority[..end];
        }

        var portSeparator = authority.LastIndexOf(':');
        var hasPort = portSeparator > authority.LastIndexOf(']');
        var host = hasPort ? authority[..portSeparator] : authority;
        var port = hasPort ? authority[(portSeparator + 1)..] : (isHttps ? "443" : "80");

        if (host is "+" or "*" or "0.0.0.0" or "[::]" or "::" or "0:0:0:0:0:0:0:0")
        {
            host = loopback;
        }

        // A bare IPv6 literal still needs its brackets to be a valid authority.
        if (host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('['))
        {
            host = $"[{host}]";
        }

        return $"{(isHttps ? "https" : "http")}://{host}:{port}{probePath}";
    }
}
