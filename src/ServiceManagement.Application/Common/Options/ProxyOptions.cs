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
}
