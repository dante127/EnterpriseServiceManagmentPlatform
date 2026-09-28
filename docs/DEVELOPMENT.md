# Development guide

Everything needed to build, run, migrate, seed and debug the platform locally.

## 1. Prerequisites

| Tool | Version | Required |
| --- | --- | --- |
| .NET SDK | 10.0+ | yes |
| SQL Server | 2022+ (or the Compose container) | yes |
| Redis | 7 (or the Compose container) | optional — the API degrades to an in-memory cache |
| Docker | any recent | only for Compose and container-backed tests |
| Git | — | yes |

Verify the SDK:

```bash
dotnet --list-sdks     # expect 10.0.x
dotnet tool restore    # installs the local dotnet-ef tool from .config/dotnet-tools.json
```

## 2. First run

### Option A — Docker Compose

```bash
cp .env.example .env
docker compose up -d
docker compose logs -f api          # migrations + seeding happen on startup
```

Swagger: <http://localhost:8080/swagger>

### Option B — Local SDK

```bash
docker compose up -d sqlserver redis

cd src/ServiceManagement.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=ServiceManagement;User Id=sa;Pwd=YourStrong!Passw0rd;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"

dotnet ef database update --project ../ServiceManagement.Infrastructure
dotnet run
```

Migrations are also applied automatically at startup when `Database:ApplyMigrationsOnStartup` is `true`.

## 3. Configuration map

| Key | Purpose | Default |
| --- | --- | --- |
| `ConnectionStrings:Default` | SQL Server connection | placeholder — must be set |
| `Jwt:Issuer` / `Jwt:Audience` | Token validation | `ServiceManagement.Api` / `ServiceManagement.Clients` |
| `Jwt:SigningKey` | HMAC-SHA256 key, **≥32 chars** | placeholder — must be set |
| `Jwt:AccessTokenMinutes` | Access token lifetime | 30 |
| `Auth:RefreshTokenDays` | Refresh token lifetime | 7 |
| `Redis:Configuration` / `Redis:Enabled` | Distributed cache | `localhost:6379` / true |
| `Database:CommandTimeoutSeconds` | EF command timeout | 30 |
| `Database:MaxRetryCount` | Transient-fault retries | 3 |
| `Database:ApplyMigrationsOnStartup` | Migrate on boot | true |
| `Sla:Fallback*` | Targets used when no policy matches | 120 / 1440 minutes, 20% |
| `BackgroundJobs:Enabled` | Start the workers | true |
| `BackgroundJobs:SlaScanIntervalMinutes` | SLA monitor cadence | 5 |
| `BackgroundJobs:RefreshTokenCleanupIntervalMinutes` | Maintenance cadence | 60 |
| `Notifications:EmailEnabled` | Send e-mail (mock transport) | false |
| `Cors:AllowedOrigins` | Browser origins; empty = deny all | `[]` |

Every one of these is bound to a strongly typed options class; nothing reads `IConfiguration` ad hoc.

## 4. Migrations

```bash
# create
dotnet ef migrations add <DescriptiveName> \
  --project src/ServiceManagement.Infrastructure \
  --startup-project src/ServiceManagement.Api \
  --output-dir Persistence/Migrations

# apply
dotnet ef database update \
  --project src/ServiceManagement.Infrastructure \
  --startup-project src/ServiceManagement.Api

# inspect the SQL that will run
dotnet ef migrations script --idempotent \
  --project src/ServiceManagement.Infrastructure \
  --startup-project src/ServiceManagement.Api

# undo the last (unapplied) migration
dotnet ef migrations remove --project src/ServiceManagement.Infrastructure --startup-project src/ServiceManagement.Api --force
```

## 5. Seed data

The seeder runs automatically in `Development` when `Users` is empty, and is a no-op otherwise.

| Account | Password | Role |
| --- | --- | --- |
| `admin@servicemanagement.local` | `Admin#Dev12345!` | Administrator |
| `manager@servicemanagement.local` | `Manager#Dev12345!` | Manager |
| `agent@servicemanagement.local` | `Agent#Dev12345!` | Agent |
| `requester@servicemanagement.local` | `Requester#Dev12345!` | Requester |

Also seeded: `Northwind Services` → `IT Operations` → `Service Desk` team; Hardware/Software/Network
categories; P1–P4 priorities with matching SLA policies (30 min/4 h for Critical, 8 h/48 h for Low); three
services; four tickets spread across the workflow and the SLA clock.

To re-seed from scratch, drop the database and let the API migrate + seed again:

```bash
dotnet ef database drop --force \
  --project src/ServiceManagement.Infrastructure \
  --startup-project src/ServiceManagement.Api
```

## 6. Running the tests

```bash
dotnet test                                   # full suite; persistence tests use LocalDB when present
```

| Project | Purpose | Needs a database |
| --- | --- | --- |
| `ServiceManagement.UnitTests` | Domain, validation, mapping, configuration files | no |
| `ServiceManagement.IntegrationTests` | Persistence, concurrency, sequences, soft delete, idempotency | yes (auto-detected) |
| `ServiceManagement.ApiTests` | Host boot, Swagger gating, auth, correlation id, full HTTP lifecycle | smoke: no · lifecycle: yes |

The persistence fixture looks for a database in this order and silently skips only if none is reachable:

1. `ESMP_TEST_SQL_CONNECTION` - an explicit connection string, which is what CI sets;
2. SQL Server LocalDB, instance `MSSQLLocalDB` - started automatically if it is installed but stopped;
3. Testcontainers, if a Docker daemon is reachable.

Set `ESMP_TEST_SKIP_LOCALDB=1` to bypass step 2. Run a single test project:

```bash
dotnet test tests/ServiceManagement.UnitTests/ServiceManagement.UnitTests.csproj
```

## 6a. Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request to `main`:

- **build** - `dotnet build --configuration Release -warnaserror`, so a new warning fails the run;
- **unit, integration and API tests** - against a SQL Server service container;
- **docker** - builds the image with buildx caching, then smoke tests the published
  artifact: it asserts Swagger is 404, runs the image's own `--health-check`, and
  confirms startup rejects the placeholder signing key.

Local reproduction of the gate:

```bash
export Jwt__Issuer=ServiceManagement.Api.Ci
export Jwt__Audience=ServiceManagement.Clients.Ci
export Jwt__SigningKey=ci-only-signing-key-that-is-long-enough-1234567890
dotnet build ServiceManagement.slnx -c Release -warnaserror
```

## 6b. Deployment

The image is built from `docker/Dockerfile` and expects these at runtime:

| Setting | Requirement |
| --- | --- |
| `Jwt__SigningKey` | **Required.** At least 32 characters and not the placeholder from `appsettings.json`; startup fails otherwise. Generate with `openssl rand -base64 48`. |
| `ConnectionStrings__Default` | **Required.** |
| `ASPNETCORE_ENVIRONMENT` | `Production` in a deployed environment; anything else publishes Swagger. |
| `Proxy__Enabled` | Set to `true` behind a reverse proxy, together with `Proxy__KnownProxies`. Without it, rate limiting and audit records see the proxy address. |
| `Cors__AllowedOrigins__0..n` | Explicit browser origins. An empty list allows none. |

Run the single-host compose stack in production mode:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

Health probes exposed by the API: `/health/live` (process is up), `/health/ready`
(SQL Server and Redis reachable), and `/health` (everything). The image's
`HEALTHCHECK` calls the application itself:

```bash
dotnet ServiceManagement.Api.dll --health-check [--health-probe /health/ready]
```

It exits non-zero when the API is not serving, which is what lets an orchestrator
restart a broken replica. The runtime image ships no `curl` or `wget`, which is why
the probe lives in the application rather than in the Dockerfile.

## 7. Project conventions

| Rule | Why |
| --- | --- |
| Nullable reference types everywhere | Nullability is explicit, not discovered in production |
| `async`/`await` end to end, `CancellationToken` threaded through | No `.Result`, no `.Wait()`, no accidental fire-and-forget |
| No business logic in controllers | Controllers only translate HTTP ⇄ MediatR requests |
| DTOs at the API boundary | EF entities never leave the infrastructure |
| Strongly typed options | No scattered `IConfiguration` keys |
| Permission-based authorization | Controllers never inspect role names |
| One handler, one `SaveChangesAsync` | Each request commits once |

Build settings shared by every project live in `Directory.Build.props`; naming and style rules in
`.editorconfig`. A few analyzer rules are deliberately suppressed and documented in that file.

## 8. Debugging tips

| Symptom | Check |
| --- | --- |
| API exits immediately at startup | Connection string missing → the clear `InvalidOperationException` names the key |
| 401 on a valid token | Signing key, issuer or audience mismatch; clock skew beyond 30 s |
| 409 on a benign update | Another writer really did change the ticket — compare `rowVersion` |
| `Pwd` masked in a log copy | Copy secrets via user secrets, never paste them into chat or tickets |
| Background job not firing | `BackgroundJobs:Enabled`, and remember each cycle needs the DB application lock |
| Swagger missing | Confirm `UseSwagger`/`UseSwaggerUI` are not disabled for the environment; they are always on here |

Logs are written to the console and to `logs/api-YYYYMMDD.log`; every line carries the `CorrelationId`
enricher, so `grep <correlationId> logs/*.log` reconstructs a full request trace together with its audit
records.

## 9. Useful commands

```bash
dotnet build                                        # whole solution
dotnet format --verify-no-changes                   # style check
dotnet ef migrations list \
  --project src/ServiceManagement.Infrastructure \
  --startup-project src/ServiceManagement.Api
docker compose ps                                   # stack health
docker compose logs -f sqlserver
```
