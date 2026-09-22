namespace ServiceManagement.Api.Configuration;

/// <summary>Names of the rate limiting policies used by sensitive endpoints.</summary>
public static class RateLimitPolicies
{
    /// <summary>Applied to credential endpoints to blunt brute-force attempts.</summary>
    public const string Authentication = "authentication";
}
