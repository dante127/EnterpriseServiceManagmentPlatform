# AGENTS.md

## What this repo is

Backend-only **Enterprise Service Management Platform**: a production-shaped ASP.NET Core 10 API for
identity, service catalog, ticket lifecycle, SLA tracking, notifications and audit. No frontend.
Clean Architecture **modular monolith** — one deployable API, one database (SQL Server required;
Redis optional, the API degrades to an in-memory cache).

## Solution & commands

Solution file is `ServiceManagement.slnx` (XML format). SDK pinned by `global.json` (.NET 10).
`dotnet-ef` is a local tool — run `dotnet tool restore` first.

```bash
dotnet build ServiceManagement.slnx -warnaserror          # warnings are errors
dotnet test tests/ServiceManagement.UnitTests             # runs anywhere, no infra needed
dotnet ef database update --project src/ServiceManagement.Infrastructure --startup-project src/ServiceManagement.Api
docker compose up -d sqlserver redis                      # local infra
dotnet run --project src/ServiceManagement.Api
```

- `TreatWarningsAsErrors` is on via `Directory.Build.props`; the build is clean today — keep it clean.
  Analyzer suppressions live there **with a reason each** (rationale in `docs/FINAL_REVIEW.md`);
  do not add or widen suppressions without one.
- Integration/API tests need real SQL Server (rowversion concurrency, filtered unique index, and the
  ticket-number sequence cannot be faked). Opt in with `ESMP_RUN_INTEGRATION_TESTS=1`; point the
  fixture at a server with `ESMP_TEST_SQL_CONNECTION` plus `ESMP_TEST_SKIP_LOCALDB=1` (this is the CI
  pattern). Without opting in they report as skipped and the suite stays green.
- **The composition root refuses to start with the placeholder `Jwt:SigningKey`.** Any test that boots
  the API needs a real key (HMAC, ≥32 chars) via user secrets or `Jwt__*` env vars (see CI workflow).
  Never commit real secrets — user secrets / `.env` only.

## Layer rules (enforced by review; some by project references)

`Domain ← Application ← Infrastructure ← Api` — dependencies point inward only.

- **Domain**: aggregates, enums, domain exceptions, `TicketWorkflow`, `SlaCalculator`, `Guard`.
  **Zero NuGet packages** — no EF Core, no ASP.NET Core.
- **Application**: MediatR handlers + FluentValidation validators, DTOs, abstractions, strongly typed
  options. Never sees `HttpContext` (uses `ICurrentUserService`) or concrete Redis/SQL. Modules are
  folders (`Tickets`, `Auth`, `Catalog`, `Organizations`, `Notifications`, `Audit`, `Reports`,
  `Users`, `Common`); cross-module interaction goes through interfaces, not each other's internals.
- **Infrastructure**: `ApplicationDbContext`, entity configurations, migrations, seeding, JWT,
  hashing, cache, background workers.
- **Api**: controllers are thin HTTP↔MediatR translators — no business logic. EF entities are never
  returned; DTOs only. Authorization is permission-based; controllers never check role names.
- Persistence is behind `IApplicationDbContext`, deliberately EF-shaped — do **not** introduce generic
  repositories/unit-of-work wrappers.
- The clock is `IDateTimeProvider` so SLA/token expiry logic is deterministic in tests.
- CQRS: commands are tracked and own side effects (audit record, cache invalidation, notifications);
  queries are `AsNoTracking` projections straight into DTOs. Every response uses the envelope
  `{"success","data","errors","correlationId"}`; failures funnel through `GlobalExceptionHandler`.

## Coding conventions

- Nullable enabled, implicit usings, **file-scoped namespaces** (editorconfig: warning).
- Prefer explicit types over `var`, except when the type is apparent from the right-hand side.
- `async`/`await` all the way down with `CancellationToken` threaded through. No `.Result`,
  no `.Wait()`, no fire-and-forget tasks.
- New configuration goes into a strongly typed options class, never scattered `IConfiguration` lookups.
- Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`), imperative mood, one logical
  change per commit.

## Known gotchas

- `Ticket.UpdateDetails` used to null SLA deadlines on every edit; it now only invalidates them when
  priority actually changes. If you touch ticket/SLA code, read `docs/SLA_BACKFILL.md` first —
  `SlaMonitorWorker` only evaluates tickets with non-null deadlines, so nulling one silently removes
  it from monitoring.
- The correlation id (`X-Correlation-Id`) ties API responses to structured logs and audit records —
  keep it flowing through new code paths.
- Migrations are applied automatically at startup when `Database:ApplyMigrationsOnStartup=true`.
- Schema changes need an EF migration; public API changes must be reflected in `docs/API.md`.

## Docs to read before touching sensitive areas

| Area | Read |
| --- | --- |
| Layering, CQRS, request flow | `docs/ARCHITECTURE.md` |
| Local run, config map, seeding | `docs/DEVELOPMENT.md` |
| Endpoint contract (update on API changes) | `docs/API.md` |
| Database schema/concurrency | `docs/DATABASE.md` |
| Auth, token rotation, audit redaction | `docs/SECURITY.md` |
| SLA deadlines repair runbook | `docs/SLA_BACKFILL.md` |
| Suppression rationale, known debt | `docs/FINAL_REVIEW.md` |
| Definition of done | `CONTRIBUTING.md` |
| Pre-deployment secrets, ops, verification steps | `docs/PRODUCTION_CHECKLIST.md` |
