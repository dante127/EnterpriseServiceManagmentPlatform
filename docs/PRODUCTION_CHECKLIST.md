# Production readiness checklist

Everything to verify before the first real deployment. The single-host shape is
`docker-compose.yml` + `docker-compose.prod.yml`; see the header of
`docker-compose.prod.yml` for what that shape does and does not cover.

## Secrets

| Item | Requirement |
| --- | --- |
| `Jwt__SigningKey` | Random, ≥32 characters, never committed. The API **refuses to start** in Production with the development key from this repository, and with the `appsettings.json` placeholder in any environment. Generate with `openssl rand -base64 48`. |
| `MSSQL_SA_PASSWORD` | SQL Server password policy (8+ chars, mixed case, digit, symbol). Production never exposes 1433 — the prod overlay closes the port. |
| `REDIS_PASSWORD` | **Required** by the prod overlay. Redis runs with `--requirepass` and is unreachable from outside the compose network. |
| `Notifications:Smtp:Password` | Mailbox credential for the SMTP sender; set only if e-mail delivery is on. |

Rotate `Jwt__SigningKey` on suspicion of compromise: rotating it invalidates every
access token at once (access tokens are short-lived); outstanding refresh tokens
must be revoked by rotating the security stamp (deactivate/reactivate users, or
add an admin-triggered global sign-out if that becomes necessary).

## E-mail

`Notifications:EmailEnabled=false` (the default) stores notifications in the
database but delivers no mail. To deliver:

```
Notifications__EmailEnabled=true
Notifications__Smtp__Host=smtp.example.com
Notifications__Smtp__Port=587
Notifications__Smtp__FromAddress=service-desk@example.com
Notifications__Smtp__UserName=service-desk@example.com
Notifications__Smtp__Password=...
```

The startup validator refuses a host without a sender address. Sending is
best-effort by design: one failed message is logged and never blocks the SLA
cycle or an API request. Verify the mailbox receives the first breach
notification after go-live.

## Observability

Set `OTEL_EXPORTER_OTLP_ENDPOINT` (e.g. `http://collector:4317`) to export traces
and metrics over OTLP. Without it the API instruments itself but sends nothing.
Before scaling out, check the dashboards exist to answer: p95 latency per route,
rate-limit rejections, background-worker tick duration, failed e-mail sends.

## Data lifecycle

| Table | Behaviour | Action needed |
| --- | --- | --- |
| `AuditLogs` | Append-only, grows without bound | Decide a retention/archive policy before year one ends (partitioning or a scheduled archive job). Do not silently delete: the trail is the product. |
| `RefreshTokens` | Expired tokens purged after 7 days by `MaintenanceCycle` | None. |
| `Notifications` | Read notifications purged after `Notifications:ReadNotificationRetentionDays` (30) | None. |
| `Tickets` | Soft-deleted, kept forever | Archive policy is a business decision. |

## Backups

- SQL Server full backup daily + transaction-log backup at least hourly. The
  database holds the only copy of the audit trail.
- Prove restore, not just backup: the SLA deadlines, the filtered unique index
  and the ticket-number sequence all live in the schema, so a restore must come
  from a backup taken **after** the migrations of that era were applied.
- The Redis cache is disposable: losing it costs a cold dashboard, nothing more.

## Networking

- TLS terminates at the load balancer; the API serves plain HTTP on 8080 and sets
  `RequireHttpsMetadata=true` for tokens, so it will not accept metadata over
  plain HTTP. Put the load balancer on it.
- Forwarded headers: behind a proxy set `Proxy__Enabled=true` and list known
  proxies (`Proxy__KnownProxies__0=...`), or clients can forge `X-Forwarded-For`
  and escape the per-IP rate limits.
- Nothing but the API port is published by the prod overlay; keep it that way
  when adding services.

## Pre-go-live verification

1. `docker compose -f docker-compose.yml -f docker-compose.prod.yml config` — no
   errors, and `ports` only on `api`.
2. Boot with `Jwt__SigningKey` unset → the container must crash-loop, not start.
3. Boot with the development key in Production → same refusal (tested in
   `ApiHardeningTests`).
4. Login, raise a ticket, trigger an SLA breach (set a policy of 1 minute), watch
   the notification row appear, the e-mail arrive, and the audit trail record all
   three.
5. Kill the API mid-write and restart: migrations are idempotent, the seeder is
   transactional, no ticket is half-created (idempotency key).
6. Run the integration suite against a copy of production data volumes once:
   `ESMP_TEST_SQL_CONNECTION=... ESMP_TEST_SKIP_LOCALDB=1 dotnet test` — an
   explicitly configured database that cannot be reached now fails the run
   instead of silently skipping.
