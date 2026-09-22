# Contributing

Thanks for taking the time to look at this project. It is a portfolio-grade
reference implementation, but the same rules apply as in a real team repository.

## Prerequisites

| Tool | Version |
| --- | --- |
| .NET SDK | 10.0 or later |
| SQL Server | 2022 (or the `sqlserver` container from `docker-compose.yml`) |
| Redis | 7 (optional for local runs; the API degrades to an in-memory cache) |
| Docker | Optional, only needed for the containerised stack and the container-backed tests |

## Getting set up

```bash
git clone <repository-url>
cd EnterpriseServiceManagmentPlatform
dotnet tool restore
dotnet restore
dotnet build
```

Set your local secrets with user secrets instead of editing `appsettings.json`:

```bash
cd src/ServiceManagement.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=ServiceManagement;User Id=sa;Pwd=<your-password>;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
```

Then apply migrations and run the API:

```bash
dotnet ef database update --project src/ServiceManagement.Infrastructure --startup-project src/ServiceManagement.Api
dotnet run --project src/ServiceManagement.Api
```

Swagger is served at `https://localhost:7xxx/swagger` in development.

## Definition of done for a change

1. `dotnet build` completes with **zero errors and zero warnings**.
2. `dotnet test` passes; new behaviour comes with tests.
3. Migrations are added for schema changes (`dotnet ef migrations add <Name>`).
4. Public API changes are reflected in `docs/API.md` and, where relevant, in Swagger XML comments.
5. No secret, connection string credential or personal path is committed.
6. Commit messages follow the conventional format used in `git log`.

## Commit messages

```
feat: add ticket bulk assignment
fix: reject duplicate idempotency keys on ticket creation
test: cover SLA breach detection boundaries
docs: document the notification pipeline
chore: bump Entity Framework Core
```

Keep commits focused: one logical change per commit, described in the imperative mood.

## Coding conventions

The repository ships a `.editorconfig`; please keep your IDE aligned with it.

Hard rules enforced by review (and mostly by the build):

- No business logic in controllers — they translate HTTP to MediatR requests.
- EF Core entities are never returned from an endpoint; only DTOs are.
- `async`/`await` all the way down, with `CancellationToken` threaded through.
- No `.Result`, no `.Wait()`, no fire-and-forget tasks.
- New configuration goes into a strongly typed options class, not `IConfiguration` lookups scattered around.
- Authorization is permission based; controllers never check role names.

## Container-backed tests

Integration and API tests that need SQL Server are skipped unless you opt in:

```bash
docker compose up -d sqlserver
ESMP_RUN_INTEGRATION_TESTS=1 dotnet test
```

Without the variable the suite stays green and those tests report as skipped.

## Reporting issues

Please include:

- the command you ran,
- the correlation id returned by the API (header `X-Correlation-Id`),
- the relevant log lines with the same correlation id.

That correlation id is what ties an API response to its structured log entries and audit records.
