using System.Diagnostics.Metrics;
using EncDotNet.S100.Rendering.Engine.Diagnostics;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// The <c>s100.render.chart.*</c> instruments of the host-neutral render entry
/// point (<see cref="ChartRenderer"/>), on the engine's meter.
/// </summary>
internal static class ChartRenderTelemetry
{
    public static readonly Counter<long> LayersSkipped =
        Telemetry.Meter.CreateCounter<long>(
            name: "s100.render.chart.layers.hidden.count",
            unit: "{layers}",
            description: "Composite layer draws skipped because one finer cell's coverage hides the whole view or tile.");

    public static readonly Histogram<double> PortrayDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.render.chart.portray.duration",
            unit: "ms",
            description: "Wall-clock time to portray every dataset of one chart render, in parallel on the work scheduler.");

    public static readonly Histogram<double> TileBlockDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.render.chart.tileblock.duration",
            unit: "ms",
            description: "Duration of one tile block (metatile) raster job.");
}
