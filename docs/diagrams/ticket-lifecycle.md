# Ticket lifecycle

## Status machine

```mermaid
stateDiagram-v2
    direction LR
    [*] --> New
    New --> Assigned : assign to user or team
    New --> InProgress : work starts immediately
    New --> Cancelled
    Assigned --> InProgress
    Assigned --> Pending : waiting on the requester
    Assigned --> Cancelled
    InProgress --> Pending
    Pending --> InProgress : requester replied
    InProgress --> Resolved
    Pending --> Resolved
    InProgress --> Cancelled
    Pending --> Cancelled
    Resolved --> Closed
    Resolved --> InProgress : reopened
    Closed --> [*]
    Cancelled --> [*]
```

| Status | Terminal | Effect on timestamps |
| --- | --- | --- |
| `New` | no | — |
| `Assigned` | no | assignment history entry appended |
| `InProgress` | no | first response stamped **once** |
| `Pending` | no | — |
| `Resolved` | no | `ResolvedAt` set; clears on reopen |
| `Closed` | **yes** | `ClosedAt` set; entity becomes read-only |
| `Cancelled` | **yes** | entity becomes read-only |

Every illegal transition raises `InvalidStatusTransitionException`, which the API returns as **409** naming
both statuses. Repeating the current status is rejected as a domain rule violation (400).

## SLA state machine

```mermaid
stateDiagram-v2
    direction LR
    [*] --> WithinSla : policy applied at creation
    WithinSla --> AtRisk : elapsed ≥ (100 − threshold)% of the active window
    WithinSla --> Breached : first-response deadline passes unanswered
    AtRisk --> Breached : deadline passes
    WithinSla --> Completed : ticket resolved
    AtRisk --> Completed : ticket resolved
    Breached --> Completed : ticket resolved
    Completed --> [*]
```

The **active window** is the first-response window until an agent responds, then the resolution window.
A missed first response is a breach even when the resolution target is still comfortably in the future.

Deadlines are recalculated whenever the ticket priority changes, and re-evaluated continuously by
`SlaMonitorWorker` for open tickets.

## Priority changes

```mermaid
flowchart LR
    P["PUT /tickets/{id} with a new priorityId"] --> C["Ticket.ChangePriority"]
    C --> CLR["clear FirstResponseDueAt / ResolutionDueAt"]
    CLR --> POL["handler loads the active SlaPolicy for the new priority"]
    POL --> RE["Ticket.ApplySlaPolicy → recompute deadlines + SLA state"]
    RE --> AUD["audit TicketUpdated with old/new priority"]
```
