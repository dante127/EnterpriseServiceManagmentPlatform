# API reference

Base path: `/api/v1` · Content type: `application/json` · Enum values travel as strings.

## Response envelope

Every response — success or failure — uses the same shape.

```json
{
  "success": true,
  "data": { },
  "message": null,
  "errors": [],
  "correlationId": "0195e2c1-..."
}
```

| Field | Notes |
| --- | --- |
| `success` | `true` only for 2xx responses |
| `data` | Payload, or `null` on failure |
| `message` | Human-readable outcome; `null` on success unless a message adds meaning |
| `errors` | Field-level errors for validation failures, otherwise empty |
| `correlationId` | Same value as the `X-Correlation-Id` response header; quote it in bug reports |

### Status codes

| Code | Meaning | Produced by |
| --- | --- | --- |
| 200 | Success | Queries, updates, deletions |
| 201 | Created | Ticket, user, team, service, category, priority, SLA policy, comment creation |
| 400 | Validation failed or domain rule violated | FluentValidation, `DomainRuleViolationException` |
| 401 | Missing, expired or invalid token | JWT challenge, `UnauthorizedException` |
| 403 | Authenticated but lacking the permission | `ForbiddenException`, policy denial |
| 404 | Resource does not exist | `NotFoundException`, unmatched route |
| 409 | Conflict: illegal transition, duplicate, stale rowversion | `ConflictException`, `InvalidStatusTransitionException`, `DbUpdateConcurrencyException` |
| 429 | Rate limit exceeded | Credential endpoints |
| 500 | Unexpected failure (internals logged, never returned) | Any unhandled exception |

### Error examples

```json
{
  "success": false,
  "data": null,
  "message": "Validation failed",
  "errors": [
    { "field": "title", "message": "Title is required." },
    { "field": "description", "message": "Description is required." }
  ],
  "correlationId": "0195e2c1-..."
}
```

```json
{
  "success": false,
  "data": null,
  "message": "Ticket status cannot change from 'New' to 'Resolved'.",
  "errors": [],
  "correlationId": "0195e2c1-..."
}
```

```json
{
  "success": false,
  "data": null,
  "message": "Ticket 'TKT-000042' was modified by another user. Reload it and try again.",
  "errors": [],
  "correlationId": "0195e2c1-..."
}
```

## Pagination

List endpoints accept `page` (default 1), `pageSize` (default 20, **clamped to 100**), `sortBy` and
`sortDescending`, and return:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 250,
  "totalPages": 13,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

## Authentication

| Method | Route | Permission | Notes |
| --- | --- | --- | --- |
| POST | `/auth/login` | anonymous | Rate limited (10/min per IP) |
| POST | `/auth/refresh` | anonymous | Rotates the refresh token; reuse revokes the family |
| POST | `/auth/logout` | anonymous | Revokes the presented refresh token; idempotent |
| POST | `/auth/change-password` | authenticated | Revokes all refresh tokens of the caller |

```bash
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@servicemanagement.local","password":"Admin#Dev12345!"}'
```

```bash
curl -X POST http://localhost:8080/api/v1/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<token>"}'
```

Authenticate subsequent calls with `Authorization: Bearer <accessToken>`.

## Users

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/users?searchTerm=&isActive=&page=&pageSize=` | `users.read` |
| GET | `/users/{id}` | `users.read` |
| POST | `/users` | `users.manage` |
| PUT | `/users/{id}/activation` | `users.manage` |
| PUT | `/users/{id}/roles` | `users.manage` |

```bash
curl -X POST http://localhost:8080/api/v1/users \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{
        "email": "new.agent@example.com",
        "userName": "nagent",
        "firstName": "Nora",
        "lastName": "Agent",
        "password": "Str0ng#Password!",
        "roles": ["Agent"]
      }'
```

Password policy: at least 12 characters with an uppercase letter, a lowercase letter, a digit and a
non-alphanumeric character.

## Organizations and teams

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/organizations` | `teams.manage` |
| POST | `/organizations` | `teams.manage` |
| POST | `/organizations/departments` | `teams.manage` |
| POST | `/organizations/teams` | `teams.manage` |
| GET | `/organizations/teams?organizationId=&searchTerm=` | `teams.manage` |
| GET | `/organizations/teams/{teamId}/members` | `teams.manage` |
| POST | `/organizations/teams/{teamId}/members` | `teams.manage` |
| DELETE | `/organizations/teams/{teamId}/members/{userId}` | `teams.manage` |

## Service catalog

| Method | Route | Permission | Notes |
| --- | --- | --- | --- |
| GET | `/catalog/services` | `catalog.read` | Cached |
| GET | `/catalog/categories` | `catalog.read` | Cached |
| GET | `/catalog/priorities` | `catalog.read` | Cached |
| GET | `/catalog/sla-policies` | `catalog.read` | Cached |
| POST | `/catalog/categories` | `catalog.manage` | |
| POST | `/catalog/priorities` | `catalog.manage` | |
| POST | `/catalog/sla-policies` | `catalog.manage` | Resolution target must be ≥ first response |
| PUT | `/catalog/sla-policies/{id}` | `catalog.manage` | Audit-logged with old/new values |
| POST | `/catalog/services` | `catalog.manage` | |
| PUT | `/catalog/services/{id}` | `catalog.manage` | |

## Tickets

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/tickets` | `tickets.read` |
| GET | `/tickets/{id}` | `tickets.read` |
| GET | `/tickets/{id}/history` | `tickets.read` |
| POST | `/tickets` | `tickets.create` |
| PUT | `/tickets/{id}` | `tickets.update` |
| POST | `/tickets/{id}/assign` | `tickets.assign` |
| POST | `/tickets/{id}/status` | `tickets.update` |
| POST | `/tickets/{id}/comments` | `tickets.comment` |
| DELETE | `/tickets/{id}` | `tickets.delete` |

### Search filters

`ticketNumber`, `title`, `status`, `priorityId`, `categoryId`, `serviceId`, `requesterId`, `assignedToId`,
`assignedTeamId`, `slaStatus`, `reportedFrom`, `reportedTo`, plus `page`, `pageSize`, `sortBy`,
`sortDescending`.

```bash
curl -G http://localhost:8080/api/v1/tickets \
  -H "Authorization: Bearer $TOKEN" \
  --data-urlencode "status=InProgress" \
  --data-urlencode "slaStatus=AtRisk" \
  --data-urlencode "sortBy=resolutionDueAt" \
  --data-urlencode "page=1" --data-urlencode "pageSize=20"
```

### Ticket projection

```json
{
  "id": "0195e2c1-...",
  "ticketNumber": "TKT-000042",
  "title": "Laptop will not boot",
  "description": "Black screen after the vendor update.",
  "serviceId": "...", "serviceName": "IT Support",
  "categoryId": "...", "categoryName": "Hardware",
  "priorityId": "...", "priorityName": "Critical",
  "status": "InProgress",
  "requesterId": "...", "requesterName": "Rita Requester",
  "assignedToId": "...", "assignedToName": "Ava Agent",
  "assignedTeamId": "...", "assignedTeamName": "Service Desk",
  "reportedAt": "2026-03-01T09:00:00+00:00",
  "firstRespondedAt": "2026-03-01T09:10:00+00:00",
  "firstResponseDueAt": "2026-03-01T09:30:00+00:00",
  "resolutionDueAt": "2026-03-01T13:00:00+00:00",
  "resolvedAt": null,
  "closedAt": null,
  "slaStatus": "WithinSla",
  "rowVersion": "AAAAAAAAB9E="
}
```

### Idempotent creation

Send `idempotencyKey` to make retries safe. A repeat call returns the **existing** ticket (200 semantics
via the same envelope) rather than creating a second one.

### Concurrency

Mutations accept `rowVersion` — the value the client last read. A stale value yields 409 and the ticket is
left untouched.

### Internal comments

`isInternal: true` requires `tickets.comment.internal`. Requesters never receive internal comments in
`GET /tickets/{id}/history`, and are never notified about them.

## Notifications

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/notifications?isRead=&page=&pageSize=` | `notifications.read` |
| POST | `/notifications/{id}/read` | `notifications.read` |
| POST | `/notifications/read-all` | `notifications.read` |

Notification types: `TicketAssigned`, `TicketReassigned`, `TicketCommentAdded`, `TicketStatusChanged`,
`SlaNearBreach`, `SlaBreached`.

## Reports

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/reports/dashboard` | `reports.read` |

Returns total/open ticket counts, resolved-last-30-days, breached and at-risk counts, mean resolution
hours, breakdowns by status/priority/SLA state, and the ten most recent tickets. Cached for 30 seconds.

## Audit

| Method | Route | Permission |
| --- | --- | --- |
| GET | `/audit?from=&to=&action=&entityName=&userId=&page=&pageSize=` | `audit.read` |
| GET | `/audit/actions` | `audit.read` |

## Health probes

| Route | Checks | Use |
| --- | --- | --- |
| `/health` | all registered checks | Manual / diagnostic |
| `/health/live` | none (process only) | Liveness probe |
| `/health/ready` | SQL Server, Redis | Readiness probe |

Each returns JSON with per-check status and duration.

## API versioning

Routes are versioned by URL segment: `/api/v1/...`. `api-supported-versions` and
`api-deprecated-versions` response headers are emitted so clients can detect change. A future `/api/v2`
can be added side by side without touching v1.
