using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs tickets damaged by the SLA invalidation bug, in which an ordinary edit
/// (one that did not change priority) nulled both deadlines. Because
/// <c>SlaMonitorWorker</c> only evaluates tickets with a
/// <c>ResolutionDueAt</c>, such a ticket silently fell out of monitoring and
/// could neither go AtRisk nor Breached.
/// <para>
/// What this migration does, and deliberately does not do:
/// </para>
/// <list type="bullet">
/// <item>Only rows with <em>both</em> deadlines null are touched. A half-populated
/// row means a different fault, and overwriting it would destroy information the
/// repair was not asked to reconstruct.</item>
/// <item>Deadlines are recomputed from the ticket's own <c>ReportedAt</c> and the
/// targets of its priority's newest active policy, matching what
/// <c>SlaCalculator.CalculateDeadlines</c> produces today.</item>
/// <item>A priority with no active policy falls back to the documented default
/// targets, because leaving a ticket without deadlines would repeat the very
/// failure being repaired.</item>
/// <item><c>SlaStatus</c> is left untouched. The monitor re-evaluates every
/// repaired ticket on its next tick, so recomputing it here would be redundant
/// and could contradict the monitor's own verdict.</item>
/// <item>Soft-deleted tickets are excluded.</item>
/// </list>
/// <para>
/// The operation is idempotent: re-running it selects no rows, because a repaired
/// ticket no longer satisfies the "both null" predicate.
/// </para>
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928000000_BackfillDamagedTicketSlaDeadlines")]
public partial class BackfillDamagedTicketSlaDeadlines : Migration
{
    /// <summary>
    /// Matches the documented fallback in <c>SlaOptions</c>. A migration cannot
    /// read bound configuration, so the values are inlined and stated here
    /// explicitly; changing them later is a data decision, not a code one.
    /// </summary>
    private const int FallbackFirstResponseMinutes = 120;

    private const int FallbackResolutionMinutes = 1440;

    /// <summary>
    /// The repair statement. Exposed so the test suite can execute the exact SQL
    /// that ships against deliberately damaged rows, rather than a hand-written
    /// copy that could drift from the migration.
    /// </summary>
    public static IReadOnlyList<string> RepairSql() => [RepairStatement];

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(RepairStatement);

    /// <summary>
    /// A single statement, so the whole repair is atomic: either every damaged row
    /// is restored or none is.
    /// </summary>
    private static string RepairStatement => $"""
        UPDATE t
        SET t.FirstResponseDueAt = DATEADD(minute, ISNULL(policy.FirstResponseMinutes, {FallbackFirstResponseMinutes}), t.ReportedAt),
            t.ResolutionDueAt = DATEADD(minute, ISNULL(policy.ResolutionMinutes, {FallbackResolutionMinutes}), t.ReportedAt)
        FROM dbo.Tickets AS t
        OUTER APPLY (
            -- Newest active policy wins, with Id as a deterministic tie-break so
            -- two rows sharing a timestamp cannot produce different results
            -- between runs. OUTER APPLY (not CROSS APPLY) because a priority with
            -- no active policy must still be repaired from the fallback.
            SELECT TOP (1) sp.FirstResponseMinutes, sp.ResolutionMinutes
            FROM dbo.SlaPolicies AS sp
            WHERE sp.PriorityId = t.PriorityId
              AND sp.IsActive = 1
            ORDER BY sp.CreatedAt DESC, sp.Id DESC
        ) AS policy
        -- Only fully stripped rows: a half-populated row is a different fault, and
        -- overwriting it would destroy information the repair cannot reconstruct.
        WHERE t.FirstResponseDueAt IS NULL
          AND t.ResolutionDueAt IS NULL
          -- Never resurrect a soft-deleted ticket.
          AND t.IsDeleted = 0;
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The repaired values are derived from ReportedAt and the policy table, so
        // rolling back cannot distinguish a restored deadline from an original
        // one. Reverting would therefore destroy correct data rather than restore
        // the previous state, and the operation is a no-op on a second run
        // regardless. Restoring from a backup is the only true rollback.
        throw new NotSupportedException(
            "BackfillDamagedTicketSlaDeadlines cannot be reverted: the values it writes are "
            + "indistinguishable from the ones it replaced. Restore from a backup instead.");
    }
}
