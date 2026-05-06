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
