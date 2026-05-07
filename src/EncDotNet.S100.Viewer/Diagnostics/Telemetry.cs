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
}
