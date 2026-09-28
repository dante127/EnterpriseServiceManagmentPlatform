using System.ComponentModel.DataAnnotations;

namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed database configuration (non-secret knobs only).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Range(1, 1800)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableSensitiveDataLogging { get; set; }

    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 3;

    [Range(0, 300)]
    public int MaxRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    /// When true the API applies pending EF migrations on startup. Disable it in
    /// environments where migrations are applied by a deployment pipeline, and in
    /// automated tests.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
