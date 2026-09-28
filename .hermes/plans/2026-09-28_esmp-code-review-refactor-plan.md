# ESMP Code Review & Refactor Plan

**Goal:** Fix the confirmed SLA data-loss bug, then clean up duplication and structural debt in the ticket pipeline.
**Scope:** `D:\ForGitUploads\EnterpriseServiceManagmentPlatform\src` (154 files, ~13.5k lines, .NET 10, Clean Architecture: Domain / Application / Infrastructure / Api)
**Baseline captured:** `dotnet build` → 0 warnings, 0 errors. `dotnet test` → 92 unit passed, 8 API passed, **6 integration tests SKIPPED**.

---

## 0. What's already good (don't touch)

Worth saying out loud so the refactor doesn't regress it:

- Clean Architecture boundaries are genuinely respected. `Domain` has zero dependencies; `Application` depends only on `Domain`.
- Optimistic concurrency is well designed (`RowVersion` + base64 echo + HTTP 409).
- The `Guard` class and pure `SlaCalculator` make the domain testable without EF.
- Background workers use `sp_getapplock` for cross-replica mutual exclusion — correct and well documented.
- `AuditService` redacts sensitive JSON before persisting.
- `IApplicationDbContext` keeps handlers off the concrete `DbContext`.
- Analyzers enabled, `AnalysisMode=Recommended`, warnings-as-errors is *off* but the build is clean anyway.
- Tests exist for the aggregate, workflow, SLA math, and mappings.

---

## P0 — Confirmed bug: ticket updates silently destroy SLA deadlines

**This is the one that matters. It is live data loss, not a style issue.**

### The mechanism

`Ticket.UpdateDetails` **always** calls `ChangePriority`, which unconditionally nulls both SLA deadlines:

`src/ServiceManagement.Domain/Tickets/Ticket.cs:107-124`
```csharp
public void UpdateDetails(string title, string description, Guid categoryId, Guid priorityId)
{
    EnsureNotTerminal();
    Title = Guard.NotBlank(title, nameof(title), 300);
    Description = Guard.NotBlank(description, nameof(description), 8000);
    CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));
    ChangePriority(priorityId);          // <-- ALWAYS runs
}

public void ChangePriority(Guid priorityId)
{
    EnsureNotTerminal();
    PriorityId = Guard.NotEmpty(priorityId, nameof(priorityId));
    // Deadline and state are derived values: they must be recomputed, not left stale.
    FirstResponseDueAt = null;           // <-- wiped
    ResolutionDueAt = null;              // <-- wiped
}
```

The handler only recomputes when the priority was *explicitly supplied and actually changed*:

`src/ServiceManagement.Application/Tickets/TicketMutations.cs:60-65`
```csharp
ticket.UpdateDetails(request.Title, request.Description, request.CategoryId, request.PriorityId ?? ticket.PriorityId);

if (request.PriorityId is not null && request.PriorityId != previous.PriorityId)
{
    await RecalculateSlaAsync(ticket, request.PriorityId.Value, cancellationToken);
}
```

### Three cases, two of them broken

| Client sends | `UpdateDetails` does | `RecalculateSlaAsync` runs? | Result |
|---|---|---|---|
| `priorityId: null` (omitted — the common case) | nulls deadlines | **no** | 🔴 **deadlines gone forever** |
| `priorityId` = current priority | nulls deadlines | **no** | 🔴 **deadlines gone forever** |
| `priorityId` = different priority | nulls deadlines | yes | ✅ correct |

So **any** title/description edit that doesn't change priority permanently strips the ticket's SLA.

### Why it fails silently and stays hidden

1. `SlaMonitorWorker` only picks up tickets matching `ticket.ResolutionDueAt != null`
   (`src/ServiceManagement.Infrastructure/BackgroundJobs/SlaMonitorWorker.cs:135-145`)
   → a stripped ticket is **permanently invisible to SLA monitoring**. It can never go AtRisk or Breached, and no notification ever fires.
2. `SlaStatus` is never reset by `ChangePriority`, so the ticket keeps its last status and the dashboard keeps reporting it as healthy.
3. The API returns HTTP 200 with a valid-looking ticket DTO. Nothing logs a warning.
4. The 6 integration tests that would catch this are the ones that get **skipped** without SQL Server.

**A ticket edited once can silently breach its SLA and nobody is ever told.**

### Second-order bug: no SLA fallback on the update path

`CreateTicket.ApplySlaPolicyAsync` (`CreateTicket.cs:212-249`) falls back to `SlaOptions` when no policy is configured. `UpdateTicket.RecalculateSlaAsync` (`TicketMutations.cs:84-101`) **just returns** if no active policy is found:

```csharp
if (policy is null)
{
    return;      // deadlines stay null — same silent failure, different trigger
}
```

So changing a ticket to a priority with no active SLA policy also permanently strips deadlines. Two code paths, two different behaviours, both wrong.

### Fix

**P0.1 — Make the domain invalidate SLA only on a real priority change**

`src/ServiceManagement.Domain/Tickets/Ticket.cs`
```csharp
public void UpdateDetails(string title, string description, Guid categoryId, Guid priorityId)
{
    EnsureNotTerminal();
    Title = Guard.NotBlank(title, nameof(title), 300);
    Description = Guard.NotBlank(description, nameof(description), 8000);
    CategoryId = Guard.NotEmpty(categoryId, nameof(categoryId));

    if (priorityId != PriorityId)
    {
        ChangePriority(priorityId);
    }
}
```

**P0.2 — Extract one shared SLA policy resolver (removes duplication *and* the second bug)**

Both handlers currently duplicate "find policy → apply → else ???" with divergent results. Create:

`src/ServiceManagement.Application/Tickets/SlaPolicyResolver.cs` (new)
```csharp
internal sealed class SlaPolicyResolver(IApplicationDbContext dbContext, IOptions<SlaOptions> options)
{
    public async Task ApplyToAsync(
        Ticket ticket,
        Guid priorityId,
        Guid? preferredPolicyId,
        CancellationToken cancellationToken)
    {
        var policy = await ResolveAsync(preferredPolicyId, priorityId, cancellationToken);

        if (policy is not null)
        {
            ticket.ApplySlaPolicy(
                policy.FirstResponseMinutes,
                policy.ResolutionMinutes,
                policy.AtRiskThresholdPercent);
            return;
        }

        // Same documented fallback as ticket creation — never leave a ticket unmeasurable.
        var fallback = options.Value;
        ticket.ApplySlaPolicy(
            fallback.FallbackFirstResponseMinutes,
            fallback.FallbackResolutionMinutes,
            fallback.AtRiskThresholdPercent);
    }
}
```

Then `CreateTicketHandler.ApplySlaPolicyAsync` and `UpdateTicketHandler.RecalculateSlaAsync` both delegate to it. Delete both private methods.

**P0.3 — Regression tests (the reason this got through)**

`tests/ServiceManagement.UnitTests/Tickets/TicketAggregateTests.cs` — add:
- `UpdateDetails_WithUnchangedPriority_PreservesSlaDeadlines`
- `UpdateDetails_WithChangedPriority_ClearsSlaDeadlines`

New `tests/ServiceManagement.UnitTests/Tickets/SlaPolicyResolverTests.cs` (or integration, see P1.4):
- `ApplyToAsync_WithNoPolicy_UsesFallbackTargets`
- `ApplyToAsync_PrefersServicePolicy_ThenPriorityPolicy_ThenFallback`

---

## P1 — Correctness & hardening

### P1.1 Swagger is exposed in every environment

`src/ServiceManagement.Api/Extensions/WebApplicationExtensions.cs:29-34` calls `app.UseSwagger()` unconditionally, while the log line in `Program.cs:33-36` claims *"Swagger is available in development"*. It isn't gated. Any production deployment publishes the full route surface and XML docs publicly.

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => { /* ... */ });
}
```

### P1.2 Rate limiting collapses to one global bucket behind a proxy

`src/ServiceManagement.Api/Extensions/ServiceCollectionExtensions.cs:66`
```csharp
partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
```
There is no `UseForwardedHeaders` anywhere in the solution. Behind nginx/load-balancer (which `docker-compose.yml` implies) every client shares the proxy's IP, so `/auth/login` gets **10 attempts per minute total across all users**, not per user. Same root cause makes `CurrentUserService.IpAddress` (`Infrastructure/Security/CurrentUserService.cs:51`) write the proxy IP into every audit record.

Add `app.UseForwardedHeaders(...)` with an explicitly configured `KnownProxies`/`KnownNetworks` (do **not** clear them — that's an IP-spoofing vector), placed as the first middleware.

### P1.3 Response envelope is duplicated in four places

The `ApiResponse` shape is hand-rolled in:
- `Api/Models/ApiResponse.cs` (canonical)
- `Api/ErrorHandling/GlobalExceptionHandler.cs:57`
- `Infrastructure/DependencyInjection.cs:164-185` (JWT `OnChallenge` + `OnForbidden` — anonymous objects)
- `Api/Extensions/WebApplicationExtensions.cs:50-56` (404 fallback — anonymous object)

Adding a field to the envelope means editing four sites, and the anonymous ones already drifted (they omit `correlationId`). Extract a single writer used by all four.

### P1.4 Integration tests never run

`tests/ServiceManagement.IntegrationTests` — 6 of 6 tests `Skip.IfNot(_fixture.IsAvailable, ...)`, so they are silently skipped whenever SQL Server is absent. The untested surface is exactly the risky part: `RowVersion`, the filtered unique index on `IdempotencyKey`, and the `TicketNumbers` sequence.

Fix by making the fixture provision a real SQL Server — Testcontainers, or a LocalDB/`sqllocaldb` instance, or an opt-in connection string so CI always has it set. A skipped test that guards your concurrency model is worse than no test, because it looks like coverage.

### P1.5 Multiple `SaveChangesAsync` per request breaks atomicity

Every mutating handler saves **three separate times**:
1. the entity change,
2. `DatabaseNotificationService.NotifyManyAsync` → its own `SaveChangesAsync`,
3. `AuditService.RecordAsync` → its own `SaveChangesAsync`.

Each is its own transaction. If the audit write fails, the ticket change is already committed and the audit trail is missing — the one thing you must never lose in an audit-grade system.

`src/ServiceManagement.Application/Common/Interfaces/IAuditService.cs` and `INotificationService.cs` should add entities to the context and let the handler's single `SaveChangesAsync` flush them. Add an explicit transaction around the handler where ordering matters.

### P1.6 Options are never validated at startup

`services.Configure<T>(section)` in `Infrastructure/DependencyInjection.cs:46-56` with no `ValidateDataAnnotations()` / `ValidateOnStart()`. A blank JWT signing key is silently accepted and produces `IssuerSigningKey = null` (line 145-147) — with `ValidateIssuerSigningKey = true`, that only blows up on the first authenticated request, not at boot.

```csharp
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```
Apply to all eight options classes.

### P1.7 `MarkAllNotificationsReadCommand` loads every row into memory

`src/ServiceManagement.Application/Notifications/NotificationQueries.cs:130-150` materialises the entire unread inbox, mutates each, then saves. A user with 10k unread notifications pulls 10k entities.

```csharp
return await _dbContext.Notifications
    .Where(n => n.UserId == userId && !n.IsRead)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(n => n.IsRead, true)
        .SetProperty(n => n.ReadAt, now), cancellationToken);
```

### P1.8 `AddComment` is the only mutation without a terminal-status guard

`Ticket.cs:237` — `UpdateDetails`, `ChangePriority`, `AssignToUser`, `AssignToTeam` all call `EnsureNotTerminal()`. `AddComment` does not, so closed and cancelled tickets accept new comments. **This may well be deliberate** (agents often annotate closed tickets) — but it's an unintentional asymmetry, not a documented decision. Decide, then either add the guard or a comment saying why not.

---

## P2 — Structure & duplication

### P2.1 Every ticket handler repeats the same six steps

`CreateTicket`, `UpdateTicket`, `AssignTicket`, `ChangeTicketStatus`, `AddTicketComment` all follow: load → `EnsureUnchanged` → mutate → `SaveChanges` → notify → audit → `LoadDetailAsync`. Five near-identical copies, each ~40 lines, each with the same opportunity to forget a step (which is how P0 happened).

Extract two collaborators rather than a generic base class:
- `TicketChangeAuditor` — owns the `AuditEntry` construction and before/after snapshotting.
- `TicketNotifier` — owns recipient resolution (requester, assignee, team members, excluding the actor) and message construction.

Handlers keep orchestration; the cross-cutting noise leaves. Guard with the unit tests you already have.

### P2.2 Multi-command files violate the project's own convention

`CreateTicket.cs` correctly holds one command. These don't:

| File | Lines | Commands |
|---|---|---|
| `Application/Tickets/TicketMutations.cs` | 385 | 4 |
| `Application/Catalog/CatalogCommands.cs` | 276 | multiple |
| `Application/Organizations/OrganizationCommands.cs` | 212 | multiple |
| `Application/Users/UserAdministration.cs` | 180 | multiple |

Split `TicketMutations.cs` into `UpdateTicket.cs`, `AssignTicket.cs`, `ChangeTicketStatus.cs`, `DeleteTicket.cs` — one file per command, matching `CreateTicket.cs`. Pure file moves, no behaviour change; easy to review.

### P2.3 Handlers resolve dependencies from the service locator in workers

`SlaMonitorWorker.RunAsync:105-113` and `MaintenanceWorker.RunAsync:85-90` pull seven services out of a scope. Legitimate in a worker, but extract a scoped `SlaCycle` / `MaintenanceCycle` service so each becomes testable without a `BackgroundService` harness.

### P2.4 Dashboard issues eight sequential round-trips

`Application/Reports/GetDashboard.cs:61-112` — three `CountAsync`, three `GroupBy`, one average, one list. Consolidate the counts into a single grouped query, or accept it given the 30s cache. Also: the cache key embeds the TTL —

```csharp
$"{CacheKeys.Dashboard}:{_redisOptions.DefaultTtlMinutes}"
```

That's a cache-busting hack dressed as a key. Use a stable key and a proper invalidation signal. And `GetOrCreateAsync` has no stampede protection, so a cold cache after deploy fans out to N concurrent dashboard builds.

### P2.5 Correlated subqueries in the ticket history projection

`Application/Tickets/TicketQueries.cs:156-181` emits three correlated subqueries per assignment row to resolve display names. One `Join` or a single `Users.Where(ids.Contains(...)).ToDictionaryAsync(...)` is one round-trip instead of 3N.

### P2.6 Redundant soft-delete filters (cosmetic)

`HasQueryFilter` already hides deleted tickets in all three configurations. These are now triple-redundant:
- `TicketQuerySpecification.cs:46` — `query.Where(ticket => !ticket.IsDeleted)`
- `TicketQueries.cs:27` — `&& !candidate.IsDeleted`

Harmless, but it implies soft-delete is optional when it isn't, which invites someone to remove the real filter later.

### P2.7 Two competing mapping strategies

`NotificationQueries.cs:59` uses in-memory Mapster `.Adapt<NotificationDto>()`; `UserQueries.cs:95` and every ticket query use explicit EF projections. Pick one. Explicit projections keep the SQL visible — I'd drop the Mapster dependency for DTOs entirely (it's only used in two places and the two `IRegister` configs are trivial).

### P2.8 Dead and misleading API surface

- `GetDashboardQuery.RefreshKey` (`GetDashboard.cs:30`) — declared, never read anywhere. Delete it.
- `Ticket.DueAt` (`Ticket.cs:78`) — a get-only alias for `ResolutionDueAt`, not mapped to the schema. Delete it.

### P2.9 Log noise

- `LoggingBehavior` emits two lines per request ("Handling" + "Handled") on top of Serilog's request logging — three lines per call. Collapse to the outcome line.
- `SlaMonitorWorker.cs:119` and `MaintenanceWorker.cs:96` log at `Information` every time a replica skips a tick. That's every tick, per replica, forever. `Debug` or rate-limit it.

---

## P3 — Polish

- **Exceptions are split across two namespaces** (`Application/Common/Exceptions` and `Domain/Exceptions`) and `GlobalExceptionHandler` imports both. Fine, but the boundary should be stated: domain rules → 4xx from `Domain`, orchestration conflicts → 4xx from `Application`.
- **`TreatWarningsAsErrors` is `false`** in `Directory.Build.props`. The build is clean today; flipping it keeps it that way.
- **No `appsettings.Production.json`** — only base + Development.
- **`RedisOptions.DefaultTtlMinutes` is used as a cache key component** (see P2.4).

---

## Suggested execution order

1. **P0.1 → P0.3** — the bug. Small, isolated, testable. Ship alone.
2. **P1.1, P1.2** — one-line config gates with real security impact.
3. **P1.5, P1.6** — atomicity and fail-fast startup. Both touch DI wiring, so do them together.
4. **P1.4** — make integration tests actually run. Do this *before* P2 so the SLA fix has a real DB behind it.
5. **P1.3, P1.7, P2.4, P2.5** — targeted performance/correctness fixes, independent of each other.
6. **P2.1, P2.2** — the structural work. Needs P0 and P1 landed first so the extracted helpers are built from correct code.
7. **P2.6–P2.9, P3** — cleanup, batch freely.

## Risks

- **P0.2 changes behaviour**: tickets that today silently lose their SLA will start showing the configured fallback deadlines. Existing rows already stripped of deadlines stay stripped — consider a one-off backfill migration to repopulate them from their priority's active policy.
- **P1.5 (single-save) is the highest-risk refactor here.** Changing when audit/notification writes flush can surface latent failures that were previously masked by the extra transaction boundaries. Do it with the integration tests from P1.4 green.
- **P1.2 forwarded headers** are a security-sensitive change. Whitelist known proxies; never call `KnownNetworks.Clear()`.
- **P2.1 risks over-abstraction.** Five handlers with genuinely different rules — extract the notification recipient logic and audit snapshotting, not the mutation flow.

## Open questions

1. Should `AddComment` be allowed on terminal tickets? (P1.8)
2. Is the fallback-SLA behaviour on priority change correct, or should it be a hard error? (P0.2)
3. Is the dashboard cache key/TTL embedding intentional?
4. Should the dashboard cache be invalidated on ticket mutation rather than expiring on a 30s TTL?
