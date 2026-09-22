using ServiceManagement.Domain.Common;

namespace ServiceManagement.Domain.Sla;

/// <summary>Deadlines derived from the SLA policy that applies to a ticket.</summary>
public sealed record SlaDeadlines(DateTimeOffset FirstResponseDueAt, DateTimeOffset ResolutionDueAt);

/// <summary>
/// Pure SLA calculation. It has no infrastructure dependency so it can be
/// exercised directly by unit tests, and it is the only place deadlines and
/// SLA states are produced.
/// </summary>
public static class SlaCalculator
{
    public static SlaDeadlines CalculateDeadlines(
        DateTimeOffset reportedAt,
        int firstResponseMinutes,
        int resolutionMinutes)
    {
        Guard.Positive(firstResponseMinutes, nameof(firstResponseMinutes));
        Guard.Positive(resolutionMinutes, nameof(resolutionMinutes));

        return new SlaDeadlines(
            reportedAt.AddMinutes(firstResponseMinutes),
            reportedAt.AddMinutes(resolutionMinutes));
    }

    /// <summary>
    /// Evaluates the SLA state at a point in time.
    /// </summary>
    /// <param name="now">Evaluation instant.</param>
    /// <param name="reportedAt">Instant the ticket was created (window start).</param>
    /// <param name="firstResponseDueAt">Deadline for the first agent response.</param>
    /// <param name="resolutionDueAt">Deadline for resolution.</param>
    /// <param name="firstRespondedAt">Actual first response, if any.</param>
    /// <param name="resolvedAt">Actual resolution, if any.</param>
    /// <param name="atRiskThresholdPercent">Portion of the window after which the state becomes AtRisk.</param>
    public static SlaStatus Evaluate(
        DateTimeOffset now,
        DateTimeOffset reportedAt,
        DateTimeOffset firstResponseDueAt,
        DateTimeOffset resolutionDueAt,
        DateTimeOffset? firstRespondedAt,
        DateTimeOffset? resolvedAt,
        int atRiskThresholdPercent)
    {
        Guard.InRange(atRiskThresholdPercent, 1, 99, nameof(atRiskThresholdPercent));

        if (resolvedAt is not null)
        {
            return SlaStatus.Completed;
        }

        // The resolution deadline is absolute: passing it is always a breach.
        if (now >= resolutionDueAt)
        {
            return SlaStatus.Breached;
        }

        // A missing first response past its deadline is a breach even if resolution is still ahead.
        if (firstRespondedAt is null && now >= firstResponseDueAt)
        {
            return SlaStatus.Breached;
        }

        var nextDeadline = firstRespondedAt is null ? firstResponseDueAt : resolutionDueAt;
        var window = nextDeadline - reportedAt;
        if (window <= TimeSpan.Zero)
        {
            return SlaStatus.Breached;
        }

        var elapsed = now - reportedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var usedPercent = elapsed.TotalSeconds / window.TotalSeconds * 100d;
        var atRiskFrom = 100d - atRiskThresholdPercent;

        return usedPercent >= atRiskFrom ? SlaStatus.AtRisk : SlaStatus.WithinSla;
    }
}
