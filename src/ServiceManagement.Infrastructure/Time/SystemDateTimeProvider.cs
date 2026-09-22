namespace ServiceManagement.Infrastructure.Time;

/// <summary>Wall-clock implementation. Registered as a singleton because it holds no state.</summary>
public sealed class SystemDateTimeProvider : Application.Common.Interfaces.IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
