using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceManagement.Application.Common.Interfaces;

namespace ServiceManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Ambient caller for tests that exercise infrastructure services directly,
/// outside an HTTP request where no principal exists.
/// </summary>
public sealed class TestCurrentUser : ICurrentUserService
{
    public Guid? UserId { get; init; }

    public string? UserName => "test";

    public string? Email => "test@servicemanagement.local";

    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlyCollection<string> Permissions => [];

    public string? IpAddress => "127.0.0.1";

    public string? CorrelationId => "test-correlation";

    public bool HasPermission(string permission) => true;

    public Guid RequireUserId() => UserId ?? Guid.CreateVersion7();
}

/// <summary>Null logger for services that only need one to satisfy a constructor.</summary>
public static class TestLoggers
{
    public static ILogger<T> For<T>() => NullLogger<T>.Instance;
}
