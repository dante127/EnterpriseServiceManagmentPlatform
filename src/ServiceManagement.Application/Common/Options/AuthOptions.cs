namespace ServiceManagement.Application.Common.Options;

/// <summary>Strongly typed authentication configuration.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenDays { get; set; } = 7;
}
