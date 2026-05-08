using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Helpers for creating the per-assembly <see cref="ActivitySource"/>
/// and <see cref="Meter"/> instances used by EncDotNet.S100 libraries.
/// </summary>
/// <remarks>
/// <para>
/// Each EncDotNet.S100 library exposes a single internal static
/// <c>Telemetry</c> class that holds its <see cref="ActivitySource"/>
/// and <see cref="Meter"/>. By convention the source / meter name is
/// the assembly name (e.g. <c>EncDotNet.S100.Pipelines</c>,
/// <c>EncDotNet.S100.Datasets.S101</c>) so consumers can subscribe to
/// the entire family with the wildcard <c>EncDotNet.S100.*</c>.
/// </para>
/// <para>
/// Versions track <see cref="AssemblyInformationalVersionAttribute"/>
/// when present, falling back to the assembly's file version. Both
/// <see cref="ActivitySource"/> and <see cref="Meter"/> are inert when
/// no listener is attached, so libraries pay no measurable cost when
/// observability is not wired up by the host.
/// </para>
/// <para><b>Canonical instrument naming conventions:</b></para>
/// <list type="bullet">
/// <item><description>
/// <b>Spans (activities):</b> <c>s100.&lt;area&gt;.&lt;verb&gt;</c> for
/// point-in-time events (e.g. <c>s100.symbol.resolve</c>,
/// <c>s100.lua.execute</c>), and <c>s100.&lt;area&gt;.&lt;noun&gt;</c>
/// for stage containers (e.g. <c>s100.pipeline.vector.stage.xslt</c>,
/// <c>s100.render.frame</c>).
/// </description></item>
/// <item><description>
/// <b>Histograms:</b> <c>s100.&lt;area&gt;.&lt;measured&gt;</c>, e.g.
/// <c>s100.pipeline.duration</c>, <c>s100.render.frame.duration</c>,
/// <c>s100.symbol.resolve.duration</c>. Use <c>ms</c> for durations,
/// <c>{items}</c> for counts.
/// </description></item>
/// <item><description>
/// <b>Counters:</b> <c>s100.&lt;area&gt;.&lt;event&gt;.count</c>, e.g.
/// <c>s100.symbol.cache.hit.count</c>,
/// <c>s100.xslt.cache.miss.count</c>.
/// </description></item>
/// </list>
/// <para>
/// All tags are defined in <see cref="TelemetryTags"/>. New instruments
/// must follow these patterns so dashboards and queries stay uniform.
/// </para>
/// </remarks>
public static class S100Telemetry
{
    /// <summary>
    /// Builds an <see cref="ActivitySource"/> whose name matches the
    /// caller's assembly name and whose version matches the assembly's
    /// informational version.
    /// </summary>
    /// <param name="markerType">A type from the assembly that owns the
    /// new source. Pass <c>typeof(Telemetry)</c> from the per-library
    /// holder class.</param>
    public static ActivitySource CreateActivitySource(Type markerType)
    {
        ArgumentNullException.ThrowIfNull(markerType);
        var assembly = markerType.Assembly;
        return new ActivitySource(GetName(assembly), GetVersion(assembly));
    }

    /// <summary>
    /// Builds a <see cref="Meter"/> whose name matches the caller's
    /// assembly name and whose version matches the assembly's
    /// informational version.
    /// </summary>
    public static Meter CreateMeter(Type markerType)
    {
        ArgumentNullException.ThrowIfNull(markerType);
        var assembly = markerType.Assembly;
        return new Meter(GetName(assembly), GetVersion(assembly));
    }

    private static string GetName(Assembly assembly) =>
        assembly.GetName().Name ?? "EncDotNet.S100";

    private static string GetVersion(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // Strip any "+commitsha" build metadata to keep the
            // semver-ish version short and dashboard-friendly.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
