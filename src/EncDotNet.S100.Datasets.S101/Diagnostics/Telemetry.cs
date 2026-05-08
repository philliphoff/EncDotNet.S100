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
}
