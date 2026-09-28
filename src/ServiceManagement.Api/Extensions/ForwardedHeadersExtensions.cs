using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ServiceManagement.Application.Common.Options;

namespace ServiceManagement.Api.Extensions;

/// <summary>
/// Applies <c>X-Forwarded-*</c> headers so the client address seen by the API is
/// the real one instead of the reverse proxy.
/// </summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Enables forwarded-header processing according to <see cref="ProxyOptions"/>.
    /// Disabled by default so a deployment reachable directly from the internet
    /// cannot have its client address spoofed.
    /// </summary>
    public static WebApplication UseForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<IOptions<ProxyOptions>>().Value;

        if (!options.Enabled)
        {
            return app;
        }

        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = Math.Max(1, options.ForwardLimit)
        };

        if (options.TrustAllForwardedHeaders)
        {
            // Deliberately unsafe: a client can then forge X-Forwarded-For and walk
            // straight past the login rate limit. Only valid on a trusted network path.
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
        }
        else
        {
            // Drop the loopback defaults so only explicitly configured proxies count.
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();

            foreach (var proxy in options.KnownProxies.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    forwarded.KnownProxies.Add(address);
                }
            }

            foreach (var network in options.KnownNetworks.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                if (System.Net.IPNetwork.TryParse(network, out var parsed))
                {
                    forwarded.KnownIPNetworks.Add(parsed);
                }
            }
        }

        app.UseForwardedHeaders(forwarded);

        return app;
    }
}
