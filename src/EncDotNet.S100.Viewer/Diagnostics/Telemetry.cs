using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Viewer-level <see cref="ActivitySource"/> and <see cref="Meter"/>.
/// Hosts the root <c>s100.viewer.command</c> spans and the
/// <c>s100.viewer.command.duration</c> histogram so a single user
/// action (open file, change palette, render) produces one causal
/// trace.
/// </summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    public static readonly Histogram<double> CommandDuration =
        Meter.CreateHistogram<double>(
            "s100.viewer.command.duration",
            unit: "ms",
            description: "Wall-clock duration of a top-level viewer command.");

    public static readonly Counter<long> ViewingGroupToggled =
        Meter.CreateCounter<long>(
            "s100.viewer.viewinggroup.toggled",
            description: "Number of times a user toggled a viewing-group override.");

    public static readonly Counter<long> DisplayPlaneToggled =
        Meter.CreateCounter<long>(
            "s100.viewer.displayplane.toggled",
            description: "Number of times a user toggled a display-plane override.");

    /// <summary>
    /// Wall-clock duration of one Mapsui paint on the compositor
    /// render thread, captured by bracketing the
    /// <c>MapsuiCustomDrawOperation</c> with marker custom draw
    /// operations. Compare against <see cref="MapPaintInterval"/>:
    /// if duration ≪ interval, paints are throttled by Avalonia's
    /// invalidation/dispatch cadence rather than by Skia rendering.
    /// </summary>
    public static readonly Histogram<double> MapPaintDuration =
        Meter.CreateHistogram<double>(
            name: "s100.map.paint.duration",
            unit: "ms",
            description: "Wall-clock duration of one Mapsui paint on the compositor render thread.");

    /// <summary>
    /// Wall-clock interval between successive paint completions on
    /// the compositor render thread. Idle gaps over 500 ms are
    /// excluded so a single pause doesn't dominate percentiles.
    /// </summary>
    public static readonly Histogram<double> MapPaintInterval =
        Meter.CreateHistogram<double>(
            name: "s100.map.paint.interval",
            unit: "ms",
            description: "Wall-clock interval between successive Mapsui paints on the compositor render thread.");
}
