using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Composition-root helper that registers OpenTelemetry tracing,
/// metrics, and logging into the viewer's <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// All <c>EncDotNet.S100.*</c> ActivitySources and Meters declared in
/// the libraries are subscribed via wildcard. The OTLP exporter is
/// configured via the standard OTEL_* environment variables
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, <c>OTEL_SERVICE_NAME</c>,
/// <c>OTEL_RESOURCE_ATTRIBUTES</c>); when no endpoint is reachable
/// the exporter retries silently and the application keeps working.
/// </remarks>
internal static class ViewerObservability
{
    private const string ServiceName = "EncDotNet.S100.Viewer";
    private const string SourceWildcard = "EncDotNet.S100.*";

    public static IServiceCollection AddS100Observability(this IServiceCollection services)
    {
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? ServiceName;
        var serviceVersion = typeof(ViewerObservability).Assembly
            .GetName().Version?.ToString() ?? "0.0.0";

        services.AddLogging(builder =>
        {
            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(serviceName: serviceName, serviceVersion: serviceVersion));
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.AddOtlpExporter();
            });
        });

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddSource(SourceWildcard)
                .AddSource(ServiceName)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(SourceWildcard)
                .AddMeter(ServiceName)
                .AddOtlpExporter());

        return services;
    }

    /// <summary>
    /// Starts a root <c>s100.viewer.command</c> activity wrapping a
    /// user action, recording <c>s100.viewer.command.duration</c> on
    /// dispose.
    /// </summary>
    public static CommandScope BeginCommand(string commandName)
    {
        var activity = Telemetry.ActivitySource.StartActivity(
            "s100.viewer.command", ActivityKind.Internal);
        activity?.SetTag("s100.viewer.command", commandName);
        return new CommandScope(activity, commandName);
    }

    internal readonly struct CommandScope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly string _commandName;
        private readonly long _startTimestamp;

        internal CommandScope(Activity? activity, string commandName)
        {
            _activity = activity;
            _commandName = commandName;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        public void SetStatus(bool ok, string? description = null)
        {
            if (_activity is null) return;
            _activity.SetStatus(ok ? ActivityStatusCode.Ok : ActivityStatusCode.Error, description);
        }

        public void Dispose()
        {
            var elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
            Telemetry.CommandDuration.Record(elapsedMs,
                new System.Collections.Generic.KeyValuePair<string, object?>(
                    "s100.viewer.command", _commandName));
            _activity?.Dispose();
        }
    }
}
