using Serilog.Context;
using ServiceManagement.Infrastructure.Diagnostics;

namespace ServiceManagement.Api.Middleware;

/// <summary>
/// Ensures every request carries a correlation id: it is reused from the
/// incoming header when present, generated otherwise, echoed back in the
/// response, pushed into the log context and stored for the audit trail.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = context.Request.Headers[CorrelationIdDefaults.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString();
        }

        context.Items[CorrelationIdDefaults.ItemsKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdDefaults.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }

        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                "Request {Method} {Path} failed with status {StatusCode} (correlation {CorrelationId}).",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                correlationId);
        }
    }
}
