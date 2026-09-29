using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Abstractions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServiceManagement.Api.Extensions;
using ServiceManagement.Application.Common.Options;
using Xunit;

namespace ServiceManagement.ApiTests.Proxy;

/// <summary>
/// The proxy settings are easy to get wrong in a way that fails silently.
/// <para>
/// Enabling forwarded-header processing with no configured proxy leaves the
/// framework's own defaults in place, which trust only the loopback interface.
/// On a container network the proxy arrives from a bridge address, so its headers
/// are discarded and every client looks like the proxy. Nothing warns about it:
/// login throttling quietly becomes a single global bucket and every audit record
/// names the proxy instead of the user.
/// </para>
/// </summary>
public class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void Proxy_options_default_to_unsafe_off()
    {
        var options = new ProxyOptions();

        options.Enabled.Should().BeFalse("an unlisted proxy is the only safe default");
        options.TrustAllForwardedHeaders.Should().BeFalse();
        options.KnownProxies.Should().BeEmpty();
        options.KnownNetworks.Should().BeEmpty();
        options.ForwardLimit.Should().Be(1);
    }

    [Fact]
    public void Enabling_the_proxy_without_listing_one_is_rejected_at_startup()
    {
        // Rather than start and silently ignore every forwarded header.
        var act = () => ProxyOptions.Validate(new ProxyOptions { Enabled = true });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Proxy*")
            .WithMessage("*KnownProxies*");
    }

    [Fact]
    public void A_network_is_acceptable_in_place_of_a_single_address()
    {
        var act = () => ProxyOptions.Validate(new ProxyOptions
        {
            Enabled = true,
            KnownNetworks = ["172.18.0.0/16"]
        });

        act.Should().NotThrow("a CIDR range is the usual answer for a container network");
    }

    [Fact]
    public void Trust_all_is_accepted_but_must_be_a_deliberate_choice()
    {
        // The escape hatch stays available for environments where the proxy
        // address genuinely cannot be known ahead of time.
        var act = () => ProxyOptions.Validate(new ProxyOptions
        {
            Enabled = true,
            TrustAllForwardedHeaders = true
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void An_unparseable_proxy_address_is_reported_rather_than_dropped()
    {
        var act = () => ProxyOptions.Validate(new ProxyOptions
        {
            Enabled = true,
            KnownProxies = ["not-an-ip-address"]
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*not-an-ip-address*");
    }

    [Fact]
    public void A_disabled_proxy_is_never_validated()
    {
        // The API may run directly, with no proxy in the path at all.
        var act = () => ProxyOptions.Validate(new ProxyOptions { Enabled = false });

        act.Should().NotThrow();
    }

    [Fact]
    public void Forwarded_headers_are_ignored_entirely_when_the_proxy_is_disabled()
    {
        var pipeline = BuildPipeline(null);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        context.Request.Scheme = "http";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        pipeline(context);

        // Untrusted, so the claim is discarded and the connection address stands.
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("10.0.0.5"));
    }

    [Fact]
    public void A_trusted_proxy_has_its_forwarded_address_applied()
    {
        // The trusted address is the proxy's; the header is the real client.
        var pipeline = BuildPipeline(["10.0.0.5"]);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        context.Request.Scheme = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        pipeline(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.9"),
            "the rate-limit key and the audit trail both read this address");
        context.Request.Scheme.Should().Be("https", "redirects must target the scheme the client used");
    }

    [Fact]
    public void An_untrusted_source_cannot_forge_the_client_address()
    {
        // The spoofing protection: a client setting X-Forwarded-For itself gets
        // its claim dropped, which is why TrustAllForwardedHeaders is dangerous.
        var pipeline = BuildPipeline(["10.0.0.5"]);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");

        pipeline(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.7"));
    }

    /// <summary>
    /// Builds a forwarded-headers pipeline. <paramref name="trustedProxies"/> is
    /// null to model "the proxy is off", which is the default.
    /// </summary>
    private static RequestDelegate BuildPipeline(string[]? trustedProxies)
    {
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };

        if (trustedProxies is not null)
        {
            foreach (var proxy in trustedProxies)
            {
                forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        }

        var services = new ServiceCollection();

        // The forwarded-headers middleware takes an ILoggerFactory.
        services.AddLogging();

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UseForwardedHeaders(forwarded);

        return app.Build();
    }
}
