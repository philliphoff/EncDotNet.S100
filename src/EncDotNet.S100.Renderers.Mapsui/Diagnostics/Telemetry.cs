using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui.Diagnostics;

/// <summary>Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Renderers.Mapsui</c>.</summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    public static readonly Histogram<double> FrameDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.frame.duration",
            unit: "ms",
            description: "Wall-clock duration of a Mapsui display-list render pass.");

    public static readonly Counter<long> InstructionsProcessed =
        Meter.CreateCounter<long>(
            name: "s100.render.instructions.processed.count",
            unit: "{instructions}",
            description: "Drawing instructions processed per Mapsui render pass.");

    public static readonly Counter<long> StylesApplied =
        Meter.CreateCounter<long>(
            name: "s100.render.styles.applied.count",
            unit: "{styles}",
            description: "Mapsui styles applied per render pass.");

    public static readonly Histogram<double> SymbolResolveDuration =
        Meter.CreateHistogram<double>(
            name: "s100.symbol.resolve.duration",
            unit: "ms",
            description: "Duration of a single SVG symbol resolution (hit, miss, or fallback).");

    public static readonly Counter<long> SymbolCacheHit =
        Meter.CreateCounter<long>(
            name: "s100.symbol.cache.hit.count",
            unit: "{hits}",
            description: "Symbol cache hits during symbol resolution.");

    public static readonly Counter<long> SymbolCacheMiss =
        Meter.CreateCounter<long>(
            name: "s100.symbol.cache.miss.count",
            unit: "{misses}",
            description: "Symbol cache misses during symbol resolution.");
}
