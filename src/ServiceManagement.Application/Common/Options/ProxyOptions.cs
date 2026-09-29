namespace ServiceManagement.Application.Common.Options;

/// <summary>
/// Reverse-proxy settings. The API is normally deployed behind nginx or a cloud
/// load balancer, so <c>X-Forwarded-*</c> headers are the only truthful source of
/// the client address.
/// <para>
/// Both the rate limiter partition key and the audit trail read
/// <c>Connection.RemoteIpAddress</c>. Without this, every client behind the proxy
/// shares one address: login throttling becomes a single global bucket and audit
/// records all name the proxy instead of the user.
/// </para>
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// Enables forwarded-header processing. Leave on when deployed behind a proxy.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Addresses of proxies whose forwarded headers are trusted. Leave empty unless
    /// <see cref="TrustAllForwardedHeaders"/> is explicitly enabled: an unlisted
    /// proxy is the only safe default, because a client can forge
    /// <c>X-Forwarded-For</c> itself.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// CIDR ranges of trusted proxies. Same warning as <see cref="KnownProxies"/>.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// Escape hatch for environments where the proxy address is not known ahead of
    /// time (ephemeral container IPs). This disables the spoofing protection, so it
    /// must stay off unless the network path is genuinely trusted.
    /// </summary>
    public bool TrustAllForwardedHeaders { get; set; }

    /// <summary>How many proxy hops to strip, matched to the number of proxies in front of the API.</summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Rejects a configuration that would enable the proxy but then ignore its
    /// headers.
    /// <para>
    /// This matters more than it looks. With no known proxy the framework falls
    /// back to trusting only the loopback interface, so on a container network -
    /// where the proxy arrives from a bridge address - every forwarded header is
    /// discarded. Nothing warns: login throttling silently collapses into one
    /// global bucket and every audit record names the proxy rather than the user.
    /// Failing at startup turns that into an explicit, fixable error.
    /// </para>
    /// </summary>
    public static void Validate(ProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return;
        }

        if (options.TrustAllForwardedHeaders)
        {
            return;
        }

        if (options.KnownProxies.Length == 0 && options.KnownNetworks.Length == 0)
        {
            throw new InvalidOperationException(
                "Proxy:Enabled is true but neither Proxy:KnownProxies nor Proxy:KnownNetworks is set. "
                + "Forwarded headers would be ignored, so the client address, the rate-limit key and "
                + "every audit record would name the proxy. Set the proxy address, or explicitly set "
                + "Proxy:TrustAllForwardedHeaders to true on a trusted network path.");
        }

        foreach (var proxy in options.KnownProxies.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!System.Net.IPAddress.TryParse(proxy, out _))
            {
                throw new InvalidOperationException(
                    $"Proxy:KnownProxies contains '{proxy}', which is not a valid IP address.");
            }
        }

        foreach (var network in options.KnownNetworks.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!System.Net.IPNetwork.TryParse(network, out _))
            {
                throw new InvalidOperationException(
                    $"Proxy:KnownNetworks contains '{network}', which is not a valid CIDR range.");
            }
        }
    }
}
