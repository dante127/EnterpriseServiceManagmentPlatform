# Background job flow

```mermaid
flowchart TB
    subgraph Host["API host"]
        H1["SlaMonitorWorker<br/>PeriodicTimer"]
        H2["MaintenanceWorker<br/>PeriodicTimer"]
    end

    H1 --> T1["every SlaScanIntervalMinutes (5)"]
    H2 --> T2["every RefreshTokenCleanupIntervalMinutes (60)"]

    T1 --> L{"DatabaseJobLock<br/>sp_getapplock 'sla-monitor'"}
    T2 --> L2{"DatabaseJobLock<br/>sp_getapplock 'maintenance'"}

    L -->|not acquired| SKIP1["log + skip tick<br/>(another replica is running)"]
    L -->|acquired| Q1["load active SLA policies<br/>→ at-risk thresholds per priority"]
    L2 -->|not acquired| SKIP2["log + skip tick"]
    L2 -->|acquired| Q2["purge refresh tokens past retention<br/>purge old read notifications"]

    Q1 --> C1["select up to 500 candidates<br/>open · due within the look-ahead · already resolved"]
    C1 --> E1["per ticket: RefreshSlaStatus(now, threshold)"]
    E1 --> D1{"state changed?"}
    D1 -->|no| NEXT["next ticket (idempotent - no notification)"]
    D1 -->|yes| N1["queue SlaNearBreach / SlaBreached<br/>for requester + assignee"]
    N1 --> NEXT
    NEXT --> S1["SaveChanges"]
    S1 --> N2["INotificationService.NotifyManyAsync<br/>(database rows)"]
    N2 --> M1{"Notifications:EmailEnabled?"}
    M1 -->|yes| EM["IEmailSender per recipient (best effort)"]
    M1 -->|no| DONE["log cycle summary"]
    EM --> DONE

    Q2 --> DONE2["log purge counts"]
    DONE --> REL["sp_releaseapplock"]
    DONE2 --> REL2["sp_releaseapplock"]
```

## Guarantees

| Property | How it is achieved |
| --- | --- |
| **Duplicate-safe across replicas** | `sp_getapplock` with a session-scoped exclusive lock; a second instance skips the tick instead of double-processing |
| **Idempotent** | Notifications are queued only when an SLA state actually changes; purges are naturally idempotent |
| **Bounded** | At most 500 candidates per tick, ordered by urgency; maintenance deletes in set-based statements |
| **Retryable** | A failed cycle is logged and retried on the next tick — it never terminates the host |
| **Observable** | Each cycle logs tickets evaluated and transitions recorded; the API exposes `/health/ready` for dependency state |
| **Non-blocking** | No HTTP request waits for job work; workers run in their own DI scopes with their own `DbContext` |

## Candidate selection

```mermaid
flowchart LR
    A["Tickets where<br/>IsDeleted = false<br/>AND ResolutionDueAt IS NOT NULL<br/>AND SlaStatus != Completed<br/>AND (ResolutionDueAt ≤ now+60min<br/>OR FirstResponseDueAt ≤ now+60min<br/>OR ResolvedAt IS NOT NULL)"] --> B["ORDER BY ResolutionDueAt<br/>TAKE 500"]
    B --> C["re-evaluate each ticket in the domain"]
```
