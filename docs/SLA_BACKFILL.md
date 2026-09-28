# SLA backfill runbook

## What this repairs

`Ticket.UpdateDetails` used to call `ChangePriority` unconditionally, which nulled
both SLA deadlines on **every** ticket edit. Because `SlaMonitorWorker` only
evaluates tickets matching `ResolutionDueAt IS NOT NULL`, such a ticket silently
fell out of monitoring: it could never go AtRisk or Breached, and no notification
ever fired. `SlaStatus` was not reset, so dashboards kept reporting it as healthy.

The code fix is in `Ticket.UpdateDetails`, which now only invalidates deadlines
when the priority genuinely changes. This document covers the **data** already
damaged before that fix.

## Before you run it

**Take a backup.** The repair is deliberately one-way — see *Rollback* below.

Preview the damage without changing anything:

```sql
SELECT COUNT(*) AS DamagedTickets
FROM dbo.Tickets
WHERE FirstResponseDueAt IS NULL
  AND ResolutionDueAt IS NULL
  AND IsDeleted = 0;
```

Also worth knowing before you run it:

- Tickets with **both** deadlines null are repaired. A ticket with only one null
  means a different fault and is deliberately left alone.
- Soft-deleted tickets are skipped.
- `SlaStatus` is **not** changed. `SlaMonitorWorker` re-evaluates every repaired
  ticket on its next tick, so the state corrects itself within one scan interval
  (default 5 minutes). No downtime is needed.
- If a priority has no active policy, the documented fallback targets are applied
  (120 minutes first response, 1440 minutes resolution). These are stated as
  constants in the migration, because a migration cannot read bound configuration.

## Applying it

The repair ships as migration `20260928000000_BackfillDamagedTicketSlaDeadlines`.

With `Database__ApplyMigrationsOnStartup: true` it runs automatically on boot.
If your deployment pipeline applies migrations as a separate gated step
(recommended for multiple replicas, since they contend for the same migration
lock), it runs with the rest of them:

```bash
dotnet ef database update \
  --project src/ServiceManagement.Infrastructure \
  --startup-project src/ServiceManagement.Api \
  --connection "<connection string>"
```

Keep `BackgroundJobs__Enabled: true` running afterwards, otherwise the repaired
tickets will be invisible to SLA monitoring for the same reason as before — the
deadlines are only half the story.

## Verifying

```sql
SELECT COUNT(*) AS StillDamaged
FROM dbo.Tickets
WHERE FirstResponseDueAt IS NULL
  AND ResolutionDueAt IS NULL
  AND IsDeleted = 0;
```

That should return `0`. The repair is idempotent: a second run selects no rows,
because a repaired ticket no longer satisfies the "both null" predicate.

## Rollback

**There is none, by design.** The values the repair writes are recomputed from
`ReportedAt` and the policy table, so after the fact they are indistinguishable
from the values they replaced. Reverting would destroy correct data rather than
restore the previous state, so `Down` throws instead of pretending otherwise.

Restoring a database backup is the only true rollback. If that is not acceptable
for your situation, the repair can be run manually against a restored copy first
to confirm the row counts before touching production.
