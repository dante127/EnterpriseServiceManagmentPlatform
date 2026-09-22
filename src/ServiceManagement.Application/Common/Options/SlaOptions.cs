namespace ServiceManagement.Application.Common.Options;

/// <summary>Fallback values used when a ticket has no SLA policy attached.</summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public int FallbackFirstResponseMinutes { get; set; } = 120;

    public int FallbackResolutionMinutes { get; set; } = 1440;

    public int AtRiskThresholdPercent { get; set; } = 20;
}
