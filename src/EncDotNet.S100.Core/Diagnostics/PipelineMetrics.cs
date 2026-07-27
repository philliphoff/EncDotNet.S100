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

    // ── Vector spatial index (issue #490) ──────────────────────────────

    /// <summary>
    /// Wall-clock duration of building a persistent spatial index over
    /// a vector source's features. Emitted once per source; tagged with
    /// <see cref="TelemetryTags.Product"/>.
    /// </summary>
    public static readonly Histogram<double> VectorIndexBuildDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.vector.index.build.duration",
            unit: "ms",
            description: "Wall-clock duration of building a spatial index over a vector source's features.");

    /// <summary>
    /// Wall-clock duration of a single extent query answered by a
    /// vector spatial index. Tagged with
    /// <see cref="TelemetryTags.Product"/> and
    /// <see cref="TelemetryTags.Result"/> (<c>hit</c> when the query
    /// returned at least one feature, <c>empty</c> otherwise).
    /// </summary>
    public static readonly Histogram<double> VectorIndexQueryDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.vector.index.query.duration",
            unit: "ms",
            description: "Wall-clock duration of a spatial-index-answered extent query.");

    /// <summary>
    /// Number of features indexed by a vector spatial index. Recorded
    /// once per build; a static-cardinality proxy for dataset
    /// complexity.
    /// </summary>
    public static readonly Histogram<long> VectorIndexFeatureCount =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.vector.index.features.count",
            unit: "{features}",
            description: "Number of features indexed by a spatial index at build time.");

    /// <summary>
    /// Number of features returned by a vector-index extent query.
    /// Combined with <see cref="VectorIndexFeatureCount"/> this
    /// exposes the culling ratio and lets dashboards flag
    /// linear-scan-equivalent queries (returned ≈ indexed).
    /// </summary>
    public static readonly Histogram<long> VectorIndexReturnedCount =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.vector.index.returned.count",
            unit: "{features}",
            description: "Number of features returned by a vector-index extent query.");
}
