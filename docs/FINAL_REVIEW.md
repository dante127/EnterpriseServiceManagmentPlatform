# Final engineering review

A read-only self-review performed after implementation, covering architecture, security, performance,
database, concurrency, testing, containers and documentation — and, importantly, the parts that are weak.
Nothing here is dressed up to make the project look finished.

## 1. Verification snapshot

| Check | Result |
| --- | --- |
| `dotnet build ServiceManagement.slnx` | **0 errors, 0 warnings** (2 analyzer rules left intentionally open are documented in `Directory.Build.props`) |
| `dotnet test` (unit) | **92 passed / 0 failed** |
| `dotnet test` (API) | **8 passed / 1 skipped / 0 failed** |
| `dotnet test` (integration) | **0 passed / 6 skipped / 0 failed** (opt-in container tests) |
| `dotnet ef migrations add InitialCreate` | Generated cleanly — 19 tables, `TicketNumbers` sequence, `rowversion`, filtered unique idempotency index, no shadow foreign keys |
| `dotnet ef migrations list` | Migration listed; **not applied to a live database in this environment** (no SQL Server available) |
| Docker Compose | Authored and reviewed, **not executed** — the Docker daemon was not running in the build environment |
| Full HTTP lifecycle test | Written, wired to the seed data, **skipped** without a container |

Where a claim above says "0 warnings", the suppression list in `Directory.Build.props` is the honest
boundary of that statement: `CA1848/CA1873` (LoggerMessage source generation for ad-hoc logging),
`CA1305` (culture-specific formatting in log strings), `CA1000` (static factories on `ApiResponse<T>`),
`CA1859` (micro-optimisations), `CA1711/CA1716` (domain naming such as `Permission`, `Service`, `Group`).

## 2. What is solid

| Area | Evidence |
| --- | --- |
| Dependency direction | `Domain` compiles with zero package references; layers reference strictly inwards |
| Workflow correctness | 20+ transition tests; invalid transitions throw `InvalidStatusTransitionException` and map to 409 |
| SLA engine | Pure `SlaCalculator` with a complete state matrix: within-SLA, at-risk, both breach modes, completion |
| Concurrency | `rowversion` plus a pre-write token check; unit test on the model, container test for the real conflict |
| Idempotency | Filtered unique index verified in the model and in a container test |
| Token security | Rotation, reuse detection, hash-only storage, revocation on password change and deactivation |
| Authorization | Dynamic permission policies; no role-name checks anywhere in controllers |
| API consistency | One envelope, one exception map, correlation ids end to end |
| Testing discipline | 100 tests across three projects, designed to be green without Docker and stronger with it |

## 3. Remaining technical debt

| Item | Impact | Suggested fix |
| --- | --- | --- |
| No transactional outbox for notifications | A crash between `SaveChangesAsync` and notification persistence loses a notification | Introduce an outbox table written in the same transaction, drained by a worker |
| E-mail is a logging implementation | E-mail is not really delivered | Implement `IEmailSender` over SMTP/SendGrid; call sites are already in place |
| Attachments not implemented | Users cannot attach files | Add blob storage + `TicketAttachment` metadata table |
| No SignalR channel | Clients poll for notifications | Add a hub that broadcasts on notification insert |
| In-process rate limiting | Limits are per instance | Redis-backed counter or ingress-level limiting |
| Hand-rolled specification | Less expressive than Ardalis for complex compositions | Swap `TicketQuerySpecification` for Ardalis if query complexity grows |

## 4. Potential bottlenecks

| Area | Risk | Current mitigation |
| --- | --- | --- |
| Ticket search | Complex filter combinations could outrun the indexes under load | Every filter column is indexed; paging and counting run in SQL. Add `INCLUDE` columns or full-text search if profiling demands it |
| Dashboard aggregate | Four grouped queries per request | Cached for 30 s; each query is a single group-by projection |
| `SlaMonitorWorker` | Very large tables would make the batch cap the throughput limit | Bounded at 500 tickets per tick by design; scale out is safe because of the application lock |
| Notification fan-out | Team-wide notifications write one row per member | Acceptable at team size; bulk insert if teams grow large |
| `AuditLogs` growth | Append-only table grows without bound | Partitioning or a retention job would be needed at scale |
| Cache stampede on cold start | Several endpoints miss the cache simultaneously | TTLs are short and payloads small; a request-coalescing lock would be the next step |

## 5. Security concerns

| Concern | Status |
| --- | --- |
| Refresh-token binding | **Open.** Tokens are rotated and reuse is detected, but they are not bound to a device or user agent |
| Rate limiting scope | **Open.** Per-instance counters; multi-replica deployments need distributed limiting |
| Secret rotation | **Partially addressed.** Security stamps invalidate tokens on password change/deactivation, but there is no admin-triggered global sign-out |
| Audit payload size | **Watch.** Old/new JSON snapshots can grow; sensitive keys are redacted, large values are not truncated |
| TLS termination | **Deployment concern.** The API expects TLS at the ingress; HSTS is not configured in the app |
| Attachment handling | **Not implemented**, so upload-related attack surface is not present either |

## 6. Scalability concerns

| Dimension | Current state | Next step |
| --- | --- | --- |
| Stateless API | Yes — no in-process session state | Horizontal scale is safe |
| Background job coordination | SQL Server application lock per cycle | Already replica-safe; add lease visibility if it needs monitoring |
| Database | Single SQL Server | Read replicas would require splitting the read path |
| Cache | Redis, single instance | Cluster mode when the working set grows |
| Observability | Logs + health checks | Add OpenTelemetry traces/metrics before scaling out |

## 7. Missing tests

| Gap | Priority |
| --- | --- |
| Container-backed run of the full HTTP lifecycle in CI | High |
| Concurrency test at the application-handler level (two `Mediator.Send` calls racing) | Medium |
| Idempotency under simultaneous (not sequential) duplicate requests | Medium |
| `SlaMonitorWorker` unit test for a state transition and notification emission | Medium |
| `AuditService` redaction unit test | Medium |
| Load profile for `GET /api/v1/tickets` with realistic data volume | Low until performance matters |

## 8. Areas for future improvement

See the README's "Future improvements" list; the top three by value are OpenTelemetry observability, a
transactional outbox for notifications, and attachment storage with presigned uploads.

## 9. Honest summary

The architecture, domain rules, security model and API contract are implemented and covered by 100 tests.
What remains unproven in this environment is **execution against real infrastructure**: the Compose stack
was not started here, the initial migration was generated but not applied to a live SQL Server, and the
container-backed integration tests plus the end-to-end HTTP lifecycle test are skipped without Docker.

Running `docker compose up -d && ESMP_RUN_INTEGRATION_TESTS=1 dotnet test` on a machine with Docker is the
last step that closes that gap.
