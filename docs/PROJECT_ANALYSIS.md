# Project analysis

Phase 1 of the development process: what the platform must do, which entities and rules that implies, and
where the risk sits. This document is the reasoning behind the design, not a restatement of the README.

## 1. Problem statement

An internal IT/service organisation needs one place to:

1. receive work (tickets) against a known catalog of services,
2. decide who owns that work (teams, agents, managers),
3. promise and measure a response and resolution time (SLA),
4. keep everyone informed (comments, notifications),
5. and be able to answer "who changed what, when, and from where" afterwards (audit).

The backend must be the source of truth for all five, because every one of them is a control an auditor
or a manager will eventually ask about.

## 2. Domain entities and relationships

| Module | Entities | Key relationships |
| --- | --- | --- |
| Identity | `User`, `Role`, `Permission`, `RefreshToken`, `UserRole`, `RolePermission` | User ↔ Role many-to-many through `UserRole`; Role ↔ Permission through `RolePermission`; User → RefreshToken one-to-many |
| Organizations | `Organization`, `Department`, `Team`, `TeamMember` | Organization → Department → Team; Team ↔ User through `TeamMember`; Team → User (manager) |
| Service catalog | `ServiceCategory`, `Service`, `ServicePriority`, `SlaPolicy` | Category self-references for hierarchy; Service → Category; SlaPolicy → Priority; Service → default priority and default SLA policy |
| Tickets | `Ticket`, `TicketComment`, `TicketAssignmentHistory` | Ticket → Service/Category/Priority/Requester/Assignee/Team; Ticket → Comments and assignment history |
| Cross-cutting | `Notification`, `AuditLog` | Notification → User; AuditLog → (loose) User, EntityName + EntityId |

`ServicePriority.Level` (1 = most urgent) is what SLA lookup keys on, so "critical" is data, not code.

## 3. Business rules that must be enforced, not documented

These are the rules that a naive implementation would leave to the UI. Each one is enforced inside the
domain or the application handler, so a new endpoint cannot accidentally bypass it.

| # | Rule | Where it lives | Failure mode if skipped |
| --- | --- | --- | --- |
| R1 | Status may only change along the workflow | `TicketWorkflow.EnsureTransition`, called by `Ticket.ChangeStatus` | Tickets jumping from `New` to `Resolved`; fake SLA wins |
| R2 | `Closed`/`Cancelled` are terminal | `Ticket.EnsureNotTerminal` | Editing closed tickets, corrupting historical SLA numbers |
| R3 | First response is stamped once | `Ticket.MarkFirstResponse` (`??=`) | SLA metrics drifting every time a ticket is touched |
| R4 | Deadlines derive from the policy, not from code | `SlaCalculator` + `SlaPolicy` rows | Policy changes requiring a deployment |
| R5 | Changing priority recomputes deadlines | `Ticket.ChangePriority` + `UpdateTicketCommandHandler` | Tickets keeping the old priority's deadline |
| R6 | Only agents may write internal comments | `TicketAccessPolicy` in the handler | Internal notes leaking to requesters |
| R7 | A ticket is never duplicated by a retry | Idempotency key + filtered unique index | Duplicate incidents from flaky networks |
| R8 | Two simultaneous edits cannot silently merge | `rowversion` + explicit pre-check | Lost updates in a multi-agent desk |
| R9 | Passwords are never stored or logged | `IPasswordHasher`; audit redaction | Credential exposure |
| R10 | Every sensitive operation is auditable | `IAuditService` call sites | No answer to "who did this" |

## 4. Module boundaries and API surface

| Module | Endpoints | Authorization |
| --- | --- | --- |
| Auth | `POST /auth/login`, `/refresh`, `/logout`, `/change-password` | anonymous (rate limited) / authenticated |
| Users | `GET /users`, `GET /users/{id}`, `POST /users`, `PUT /users/{id}/activation`, `PUT /users/{id}/roles` | `users.read`, `users.manage` |
| Organizations | `GET /organizations`, `POST /organizations`, `/departments`, `/teams`, team membership endpoints | `teams.manage` |
| Catalog | `GET /catalog/{services,categories,priorities,sla-policies}`, `POST` equivalents, `PUT /catalog/sla-policies/{id}` | `catalog.read`, `catalog.manage` |
| Tickets | create, get, search, update, assign, status, comments, history, delete | `tickets.*` permissions |
| Notifications | list, mark read, mark all read | `notifications.read` |
| Reports | `GET /reports/dashboard` | `reports.read` |
| Audit | `GET /audit`, `GET /audit/actions` | `audit.read` |

Every route is declared as `api/v{version:apiVersion}/...` so `/api/v2` can be added without breaking clients.

## 5. Security requirements

- Authenticate with short-lived access tokens; refresh with rotating, revocable, hashed refresh tokens.
- Authorize on **permissions**, never on role names, so a role's meaning can change without code edits.
- Never disclose internal exception detail; never include secrets in logs or audit payloads.
- Rate limit credential endpoints; fail closed on CORS.
- Keep secrets out of source control entirely; document the environment variables instead.

## 6. Infrastructure requirements

| Requirement | Decision |
| --- | --- |
| Relational store with real concurrency control | SQL Server (rowversion, sequences, filtered indexes) |
| Cache for read-heavy, low-churn data | Redis via `IDistributedCache`, with in-memory fallback |
| Background work | `BackgroundService` workers + `sp_getapplock` for replica safety |
| Structured logs | Serilog with correlation ids |
| Probes | `/health`, `/health/live`, `/health/ready` |
| Repeatable environments | Docker multi-stage image + Compose stack |

## 7. Performance risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Ticket list becoming a full-table scan | Filter and sort columns are indexed; count and page run in SQL |
| N+1 queries from lazy loading | Lazy loading is not enabled; every read projects explicitly |
| Entity graphs dragged into memory | Queries project into DTOs; tracking disabled on reads |
| Unbounded result sets | `pageSize` clamped to 100; SAP-style "load everything" paths do not exist |
| Cache stampede on the dashboard | 30-second TTL plus cache-aside fallback, and the dashboard is read-only |
| Background job overload | Batch cap of 500 tickets per tick, application-lock single-runner |
| Chatty dashboard aggregate | One grouped query per dimension, all in SQL |

## 8. Testing strategy

| Layer | Approach |
| --- | --- |
| Domain | Pure unit tests: SLA matrix, workflow table, aggregate invariants |
| Application | Validator tests, permission policy tests, specification filter tests |
| Persistence | Container-backed: round-trips, rowversion conflicts, soft-delete visibility, index enforcement |
| HTTP | `WebApplicationFactory` smoke tests (no infrastructure) plus a full lifecycle test (with container) |
| Model | EF model built against the SQL Server provider offline to catch configuration regressions |

Container-dependent tests are opt-in (`ESMP_RUN_INTEGRATION_TESTS=1`) so the suite stays green on a
machine without Docker while still providing the stronger signal in CI.

## 9. Assumptions recorded

Where the requirements were ambiguous, these choices were made and are documented rather than hidden:

1. **Ticket number format** — sequential `TKT-000001` from a database sequence; human-readable and atomic.
2. **SLA e-mail** — off by default (`Notifications:EmailEnabled=false`); database notifications are the
   mandatory channel.
3. **Fallback SLA targets** — if no policy exists for a priority, documented `Sla:Fallback*` values apply
   instead of failing ticket creation.
4. **Soft delete applies to tickets only.** Users are deactivated rather than deleted, preserving history.
5. **Teams are queues, assignees are owners** — a ticket can be assigned to both, and assignment history
   records both dimensions.
6. **`AtRisk` is a percentage of the active window**, configurable per SLA policy, defaulting to 20%.
