using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>Fallback values used when a ticket has no SLA policy attached.</summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    [Range(1, 10080)]
    public int FallbackFirstResponseMinutes { get; set; } = 120;

    [Range(1, 525600)]
    public int FallbackResolutionMinutes { get; set; } = 1440;

    /// <summary>
    /// Percentage of the SLA window still remaining when a ticket becomes AtRisk;
    /// 20 flags a ticket once 80% of its window has elapsed.
    /// </summary>
    [Range(1, 99)]
    public int AtRiskThresholdPercent { get; set; } = 20;
}
