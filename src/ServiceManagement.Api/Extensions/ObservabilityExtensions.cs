using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ServiceManagement.Api.Extensions;

/// <summary>
/// Traces and metrics via OpenTelemetry. Instrumentation is always on (it is
/// nearly free with no exporter attached); exporting happens only when an OTLP
/// endpoint is configured, so a local run sends nothing and a production deploy
/// switches on observability with one environment variable:
///   OTEL_EXPORTER_OTLP_ENDPOINT=http://collector:4317
/// </summary>
public static class ObservabilityExtensions
{
    private const string ServiceName = "ServiceManagement.Api";

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter();
                }
            });

        return services;
    }
}
