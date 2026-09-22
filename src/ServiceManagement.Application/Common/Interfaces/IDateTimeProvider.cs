namespace ServiceManagement.Application.Common.Interfaces;

/// <summary>
/// Clock abstraction. Domain and application code never call
/// <c>DateTimeOffset.UtcNow</c> directly, which is what makes time-dependent
/// behaviour (SLA, token expiry) deterministic under test.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
