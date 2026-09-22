namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed authentication configuration.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Maximum number of active refresh tokens retained per user.</summary>
    public int MaxRefreshTokensPerUser { get; set; } = 10;
}
