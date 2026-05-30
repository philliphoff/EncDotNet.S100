using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Datasets.S101.Diagnostics;

/// <summary>Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Datasets.S101</c>.</summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    public static readonly Histogram<double> LuaExecuteDuration =
        Meter.CreateHistogram<double>(
            name: "s100.lua.execute.duration",
            unit: "ms",
            description: "Wall-clock duration of the Lua portrayal execution pass.");

    public static readonly Counter<long> LuaFeaturesCount =
        Meter.CreateCounter<long>(
            name: "s100.lua.features.count",
            unit: "{features}",
            description: "Number of features processed by the Lua executor per pass.");

    public static readonly Histogram<long> LuaInstructionsEmittedCount =
        Meter.CreateHistogram<long>(
            name: "s100.lua.instructions.emitted.count",
            unit: "{instructions}",
            description: "Drawing instructions emitted by the Lua executor per pass.");

    /// <summary>
    /// Number of drawing instructions emitted by the Lua executor for a
    /// single feature type within one portrayal pass. Tagged with
    /// <c>s100.feature.type</c> (FC code, e.g. <c>DEPCNT</c>,
    /// <c>BCNCAR</c>) and <c>s100.product</c>.
    /// </summary>
    /// <remarks>
    /// <b>Cardinality, not timing.</b> This metric reports output
    /// volume — it cannot prove a feature type consumed proportional CPU
    /// time. To attribute time per feature type, combine this metric
    /// with a sampled CPU profile collected via PerfRunner's
    /// <c>--profile cpu</c> flag.
    /// </remarks>
    public static readonly Histogram<long> LuaFeatureInstructionsCount =
        Meter.CreateHistogram<long>(
            name: "s100.lua.feature.instructions.count",
            unit: "{instructions}",
            description: "Drawing instructions emitted by the Lua executor for a single feature type per pass (cardinality, not timing). Tagged with s100.feature.type and s100.product.");
}
