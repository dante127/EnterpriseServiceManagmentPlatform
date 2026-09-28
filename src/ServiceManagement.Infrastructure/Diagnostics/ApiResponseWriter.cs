using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ServiceManagement.Infrastructure.Diagnostics;

/// <summary>
/// Single writer for the JSON response contract.
/// <para>
/// Four call sites used to hand-roll the same envelope: the global exception
/// handler, the JWT challenge/forbidden handlers and the unmatched-route
/// fallback. The hand-rolled copies had already drifted - two of them dropped the
/// correlation id - so the shape is defined once here and reused everywhere.
/// </para>
/// <para>
/// Lives in Infrastructure because both the API host and the authentication
/// wiring need it, and Infrastructure must not depend on the API project.
/// </para>
/// </summary>
public static class ApiResponseWriter
{
    /// <summary>Writes a failure envelope using the shared contract.</summary>
    public static Task WriteFailureAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken = default)
        => WriteAsync(
            context,
            statusCode,
            new
            {
                success = false,
                data = (object?)null,
                message,
                errors = Array.Empty<object>(),
                correlationId = CorrelationIdOf(context)
            },
            cancellationToken);

    private static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        object payload,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(payload, cancellationToken);
    }

    private static string? CorrelationIdOf(HttpContext context)
        => context.Items.TryGetValue(CorrelationIdDefaults.ItemsKey, out var value)
            ? value as string
            : null;
}
