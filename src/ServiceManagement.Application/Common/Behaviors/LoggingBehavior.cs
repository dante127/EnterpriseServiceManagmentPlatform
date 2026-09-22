using MediatR;
using Microsoft.Extensions.Logging;

namespace ServiceManagement.Application.Common.Behaviors;

/// <summary>
/// Emits one structured log entry per handled request (duration + outcome).
/// Correlation ids are attached by the logging configuration, not here.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var response = await next(cancellationToken);
            _logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds} ms",
                requestName,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            return response;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed {RequestName} after {ElapsedMilliseconds} ms",
                requestName,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}
