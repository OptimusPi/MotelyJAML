using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Motely.ServiceDefaults;

/// <summary>
/// Aspire's service defaults for a console app with no generic host: traces and metrics, exported
/// over OTLP to the Aspire dashboard. Aspire sets <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, the service
/// name, the resource attributes and short export intervals on every resource it starts, and the
/// OpenTelemetry SDK reads all of them from the environment. Without an endpoint nothing is
/// exported and nothing is started.
/// </summary>
public static class Extensions
{
    /// <summary>The .NET runtime's own meter: GC, thread pool, CPU and memory.</summary>
    private const string RuntimeMeter = "System.Runtime";

    /// <summary>
    /// Starts exporting the given activity sources and meters, plus runtime metrics, when an OTLP
    /// endpoint is configured. Dispose the result before exit: that flushes what is still buffered.
    /// </summary>
    /// <returns>The running exporters, or null when no OTLP endpoint is configured.</returns>
    public static IDisposable? AddServiceDefaults(IReadOnlyCollection<string> activitySources, IReadOnlyCollection<string> meters)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
            return null;

        var tracing = Sdk.CreateTracerProviderBuilder()
            // Every trace, as the web template does in development: these are local runs.
            .SetSampler(new AlwaysOnSampler())
            .AddSource([.. activitySources])
            .AddOtlpExporter()
            .Build();
        var metrics = Sdk.CreateMeterProviderBuilder()
            .AddMeter([.. meters, RuntimeMeter])
            .AddOtlpExporter()
            .Build();
        return new Exporters(tracing, metrics);
    }

    private sealed class Exporters(TracerProvider tracing, MeterProvider metrics) : IDisposable
    {
        public void Dispose()
        {
            tracing.Dispose();
            metrics.Dispose();
        }
    }
}
