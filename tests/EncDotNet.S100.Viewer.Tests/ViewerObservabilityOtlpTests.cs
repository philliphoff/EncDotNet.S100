using EncDotNet.S100.Viewer.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers registering the OTLP exporter only when an endpoint is
/// configured, so a run without a collector does not retry exports in the
/// background, and the always-on rolling log registration.
/// </summary>
public class ViewerObservabilityOtlpTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    private static bool HasOtlpServices(IServiceCollection services) =>
        services.Any(d =>
            (d.ServiceType.FullName?.Contains("Otlp", StringComparison.Ordinal) ?? false)
            || d.ServiceType.GenericTypeArguments.Any(t =>
                t.FullName?.Contains("Otlp", StringComparison.Ordinal) ?? false));

    [Fact]
    public void IsOtlpEndpointConfigured_FalseWithoutEndpoint()
    {
        Assert.False(ViewerObservability.IsOtlpEndpointConfigured(Env(), "TRACES"));
        Assert.False(ViewerObservability.IsOtlpEndpointConfigured(
            Env(("OTEL_EXPORTER_OTLP_ENDPOINT", "  ")), "TRACES"));
    }

    [Fact]
    public void IsOtlpEndpointConfigured_TrueWithGeneralEndpoint() =>
        Assert.True(ViewerObservability.IsOtlpEndpointConfigured(
            Env(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")), "LOGS"));

    [Fact]
    public void IsOtlpEndpointConfigured_HonoursSignalSpecificEndpoint()
    {
        var env = Env(("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT", "http://localhost:4318/v1/metrics"));

        Assert.True(ViewerObservability.IsOtlpEndpointConfigured(env, "METRICS"));
        Assert.False(ViewerObservability.IsOtlpEndpointConfigured(env, "TRACES"));
    }

    [Fact]
    public void AddS100Observability_DoesNotRegisterOtlpWithoutEndpoint()
    {
        var services = new ServiceCollection();

        services.AddS100Observability(logFilePath: null, verbose: false, getEnvironmentVariable: Env());

        Assert.False(HasOtlpServices(services));
    }

    [Fact]
    public void AddS100Observability_RegistersOtlpWithEndpoint()
    {
        var services = new ServiceCollection();

        services.AddS100Observability(
            logFilePath: null,
            verbose: false,
            getEnvironmentVariable: Env(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")));

        Assert.True(HasOtlpServices(services));
    }

    [Fact]
    public void AddS100Observability_WritesRollingLogsWhenDirectoryGiven()
    {
        var directory = Path.Combine(Path.GetTempPath(), "s100-rolling-log-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddS100Observability(
                logFilePath: null, verbose: false, rollingLogDirectory: directory, getEnvironmentVariable: Env());

            using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Test").LogWarning("rolling log entry");
            }

            var file = Assert.Single(Directory.GetFiles(directory, "viewer-*.log"));
            Assert.Contains("rolling log entry", File.ReadAllText(file));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
