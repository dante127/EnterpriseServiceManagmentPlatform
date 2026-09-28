using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed background worker configuration.</summary>
public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;

    [Range(1, 1440)]
    public int SlaScanIntervalMinutes { get; set; } = 5;

    [Range(1, 10080)]
    public int RefreshTokenCleanupIntervalMinutes { get; set; } = 60;
}
