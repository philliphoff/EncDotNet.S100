using System.Diagnostics.Metrics;

namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Pipeline-level metrics emitted by <see cref="EncDotNet.S100.Pipelines.Vector.VectorPipeline"/>
/// and <see cref="EncDotNet.S100.Pipelines.Coverage.CoveragePipeline"/>.
/// </summary>
/// <remarks>
/// Registered against <see cref="Telemetry.Meter"/> so all
/// <c>EncDotNet.S100.Core</c> instruments share a single meter name and
/// can be subscribed to in one call.
/// </remarks>
internal static class PipelineMetrics
{
    public static readonly Histogram<double> Duration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.pipeline.duration",
            unit: "ms",
            description: "Wall-clock duration of a portrayal pipeline pass, tagged by stage and product.");

    public static readonly Histogram<long> FeaturesIn =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.pipeline.features.in",
            unit: "{features}",
            description: "Number of distinct feature types fed into the vector pipeline per pass.");

    public static readonly Histogram<long> InstructionsOut =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.pipeline.drawinginstructions.out",
            unit: "{instructions}",
            description: "Number of drawing instructions emitted by the vector pipeline per pass.");

    public static readonly Histogram<long> CoverageCells =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.coverage.cells",
            unit: "{cells}",
            description: "Number of grid cells produced by the coverage pipeline per pass (rows × columns of the sampled region).");

    public static readonly Histogram<double> StageDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.pipeline.stage.duration",
            unit: "ms",
            description: "Wall-clock duration of an individual pipeline stage (vector or coverage).");

    public static readonly Histogram<long> StageInstructionsCount =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.pipeline.stage.instructions.count",
            unit: "{instructions}",
            description: "Drawing instructions present at the end of a pipeline stage that produces instructions.");

    public static readonly Histogram<double> XsltTransformDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.xslt.transform.duration",
            unit: "ms",
            description: "Wall-clock duration of a single XSLT rule transform pass.");

    public static readonly Histogram<double> XsltCompileDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.xslt.compile.duration",
            unit: "ms",
            description: "Wall-clock duration of compiling an XSLT rule (first load only).");

    public static readonly Counter<long> XsltCacheHit =
        Telemetry.Meter.CreateCounter<long>(
            name: "s100.xslt.cache.hit.count",
            unit: "{hits}",
            description: "XSLT compiled-transform cache hits.");

    public static readonly Counter<long> XsltCacheMiss =
        Telemetry.Meter.CreateCounter<long>(
            name: "s100.xslt.cache.miss.count",
            unit: "{misses}",
            description: "XSLT compiled-transform cache misses (compilation triggered).");
}
