# Deploying behind a reverse proxy

The API reads `Connection.RemoteIpAddress` in three places that matter:

- the **login rate limiter**, partitioned by address,
- the **audit trail**, which records who made each change,
- `CurrentUserService.IpAddress`, used in the same audit records.

Behind a reverse proxy every one of those sees the *proxy's* address, so login
throttling collapses into a single global bucket and every audit record names
the proxy instead of the user. This document covers turning that on correctly.

## The rule

Only forward headers from an address you control. A client can send
`X-Forwarded-For` itself, so a trusted-anywhere configuration lets anyone forge
the address that rate limiting and auditing depend on.

## Configuration

Set these as environment variables (or user secrets in development):

```bash
# Turn it on
Proxy__Enabled=true

# Then list the proxy. Use whichever applies to your network.
Proxy__KnownProxies__0=10.0.0.5              # a single address
# Proxy__KnownNetworks__0=172.18.0.0/16     # or a CIDR range

# Number of proxy hops between the client and the API. One proxy = 1 (the default).
# Two proxies (a load balancer plus nginx) = 2.
Proxy__ForwardLimit=1
```

**`Enabled=true` with an empty `KnownProxies`/`KnownNetworks` now fails at
startup.** That combination would fall back to trusting only the loopback
interface, so on a container network every forwarded header would be discarded
and you would be back to the broken behaviour — silently. The failure is
deliberate.

## Finding your proxy's address

If you do not know it, log in on the host and read the address the API sees:

```bash
# The address the API currently believes the client is coming from.
curl -s -o /dev/null -w '%{remote_ip}\n' https://your-api/health/live
```

- **Docker Compose, one API service:** the proxy arrives from the bridge
  network. `Proxy__KnownNetworks__0=172.16.0.0/12` covers the default Docker
  bridge ranges; tighten it to the actual subnet with `docker network inspect`.
- **Kubernetes ingress-nginx:** use the pod or node CIDR the ingress controller
  connects from, or the node network range.
- **nginx on the same host:** `Proxy__KnownProxies__0=127.0.0.1` when nginx
  proxies over loopback. If it connects over the LAN interface, use that
  interface's address instead.
- **Cloud load balancer (AWS ALB, Azure LB, GCP):** use the documented
  published-IP ranges for your provider as `KnownNetworks`.

## Verifying it worked

Add a temporary request log line, or check the audit trail. A correct
deployment shows a real client address:

```
10.1.4.77   <- the user's machine
```

If you see the proxy's address instead, the proxy is not in the trusted list, or
`ForwardLimit` is wrong for the number of hops.

## The escape hatch

```bash
Proxy__TrustAllForwardedHeaders=true
```

This trusts headers from **any** source, removing the spoofing protection
entirely. It exists for environments where the proxy address genuinely cannot
be known in advance — ephemeral container IPs, for example.

**Do not use it on a network where a client can reach the API directly.** A
single forged `X-Forwarded-For` header then walks straight past the login rate
limit and poisons the audit trail. If you use it, the API must be reachable
only through the proxy.

## TLS

TLS is expected to terminate at the proxy. The API sets neither HSTS nor an
HTTPS redirect, so the proxy must be the thing that redirects plain HTTP —
otherwise a client can bypass the redirect by connecting directly.
