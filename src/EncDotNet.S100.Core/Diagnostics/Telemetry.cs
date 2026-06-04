using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/>
/// for <c>EncDotNet.S100.Core</c>. Other libraries follow the same
/// pattern (a static <c>Telemetry</c> class in their own namespace).
/// </summary>
/// <remarks>
/// Held as <see langword="static"/> singletons so callers do not need
/// to thread the source / meter through DI. Both types are thread-safe
/// and inert when no listener is attached.
/// </remarks>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    // ── Lua portrayal (S-100 Part 9A) ──────────────────────────────────
    // These instruments are product-agnostic: the unified Core
    // LuaRuleExecutor records them for every Lua-portrayed product
    // (S-101, S-131, S-57-as-S-101) and tags each measurement with the
    // product via s100.product, so a single meter serves all products.

    /// <summary>Wall-clock duration of the Lua portrayal execution pass.</summary>
    public static readonly Histogram<double> LuaExecuteDuration =
        Meter.CreateHistogram<double>(
            name: "s100.lua.execute.duration",
            unit: "ms",
            description: "Wall-clock duration of the Lua portrayal execution pass.");

    /// <summary>Number of features processed by the Lua executor per pass.</summary>
    public static readonly Counter<long> LuaFeaturesCount =
        Meter.CreateCounter<long>(
            name: "s100.lua.features.count",
            unit: "{features}",
            description: "Number of features processed by the Lua executor per pass.");

    /// <summary>Drawing instructions emitted by the Lua executor per pass.</summary>
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
    /// with a sampled CPU profile.
    /// </remarks>
    public static readonly Histogram<long> LuaFeatureInstructionsCount =
        Meter.CreateHistogram<long>(
            name: "s100.lua.feature.instructions.count",
            unit: "{instructions}",
            description: "Drawing instructions emitted by the Lua executor for a single feature type per pass (cardinality, not timing). Tagged with s100.feature.type and s100.product.");
}
