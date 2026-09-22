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
dotnet test                                   # full suite; container tests skip
ESMP_RUN_INTEGRATION_TESTS=1 dotnet test      # also spins up a SQL Server container
```

| Project | Purpose | Needs a container |
| --- | --- | --- |
| `ServiceManagement.UnitTests` | Domain, validation, mapping, EF model configuration | no |
| `ServiceManagement.IntegrationTests` | Persistence, concurrency, sequences, soft delete, idempotency | yes (opt-in) |
| `ServiceManagement.ApiTests` | Host boot, Swagger, auth, correlation id, full HTTP lifecycle | smoke: no · lifecycle: opt-in |

The container-backed fixture starts `mcr.microsoft.com/mssql/server:2022-latest`, applies migrations and
seeds. If Docker is unavailable the tests report as **skipped**, not failed.

Run a single test project:

```bash
dotnet test tests/ServiceManagement.UnitTests/ServiceManagement.UnitTests.csproj
```

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
