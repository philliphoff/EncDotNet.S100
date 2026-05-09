using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace EncDotNet.S100.Diagnostics.Export;

/// <summary>
/// Extension methods for wiring the file-based telemetry exporters into
/// the OpenTelemetry <see cref="TracerProviderBuilder"/> and
/// <see cref="MeterProviderBuilder"/>.
/// </summary>
public static class FileExporterExtensions
{
    /// <summary>
    /// The environment variable that, when set to a file path, activates
    /// the in-process file exporters for both traces and metrics.
    /// </summary>
    public const string FileExportEnvVar = "ENC_DOTNET_OTEL_FILE";

    /// <summary>
    /// Adds a <see cref="FileTelemetryExporter"/> that writes span data
    /// to the specified <paramref name="path"/> as newline-delimited JSON.
    /// </summary>
    public static TracerProviderBuilder AddFileExporter(
        this TracerProviderBuilder builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return builder.AddProcessor(
            new OpenTelemetry.SimpleActivityExportProcessor(new FileTelemetryExporter(path)));
    }

    /// <summary>
    /// Adds a <see cref="FileMetricsExporter"/> that writes metric
    /// samples to the specified <paramref name="path"/> as
    /// newline-delimited JSON.
    /// </summary>
    public static MeterProviderBuilder AddFileExporter(
        this MeterProviderBuilder builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return builder.AddReader(
            new PeriodicExportingMetricReader(new FileMetricsExporter(path), exportIntervalMilliseconds: 5000));
    }

    /// <summary>
    /// Conditionally adds the file trace exporter when the
    /// <see cref="FileExportEnvVar"/> environment variable is set.
    /// Returns the resolved path (or <c>null</c> if not set).
    /// </summary>
    public static TracerProviderBuilder AddFileExporterIfConfigured(
        this TracerProviderBuilder builder, out string? resolvedPath)
    {
        resolvedPath = Environment.GetEnvironmentVariable(FileExportEnvVar);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            builder.AddFileExporter(resolvedPath);
        }
        return builder;
    }

    /// <summary>
    /// Conditionally adds the file metrics exporter when the
    /// <see cref="FileExportEnvVar"/> environment variable is set.
    /// </summary>
    public static MeterProviderBuilder AddFileExporterIfConfigured(
        this MeterProviderBuilder builder)
    {
        var path = Environment.GetEnvironmentVariable(FileExportEnvVar);
        if (!string.IsNullOrWhiteSpace(path))
        {
            builder.AddFileExporter(path);
        }
        return builder;
    }
}
