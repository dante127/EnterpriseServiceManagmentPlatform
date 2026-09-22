namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed background worker configuration.</summary>
public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;

    public int SlaScanIntervalMinutes { get; set; } = 5;

    public int RefreshTokenCleanupIntervalMinutes { get; set; } = 60;
}
