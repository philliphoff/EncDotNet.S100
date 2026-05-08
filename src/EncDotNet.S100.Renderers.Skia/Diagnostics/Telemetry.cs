using System.Diagnostics;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Renderers.Skia.Diagnostics;

/// <summary>
/// Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Renderers.Skia</c>.
/// </summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    public static readonly Histogram<double> CoverageFrameDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.coverage.duration",
            unit: "ms",
            description: "Wall-clock duration of a Skia coverage render pass.");

    public static readonly Counter<long> CoverageCellsProcessed =
        Meter.CreateCounter<long>(
            name: "s100.coverage.cells.processed.count",
            unit: "{cells}",
            description: "Grid cells processed per Skia coverage render pass.");
}
