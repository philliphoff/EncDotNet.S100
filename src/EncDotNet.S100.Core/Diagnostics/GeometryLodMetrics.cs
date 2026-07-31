using System.Diagnostics.Metrics;

namespace EncDotNet.S100.Diagnostics;

/// <summary>
/// Telemetry for the precomputed line LOD pyramid introduced by issue #489.
/// Instruments are exposed on the shared <see cref="Telemetry.Meter"/> so
/// they appear alongside the rest of the S-100 pipeline metrics under a
/// single meter name.
/// </summary>
/// <remarks>
/// <para>
/// This class is <em>public</em> because the renderer assembly
/// (<c>EncDotNet.S100.Renderers.Mapsui</c>) records LOD cache hits and
/// misses; the pyramid-build histogram is recorded from Core when
/// <see cref="EncDotNet.S100.Pipelines.Vector.Caching.LineLodPyramid.Build"/>
/// runs.
/// </para>
/// <para>
/// The renderer's existing <c>s100.simplify.cache.*</c> counters (in
/// <c>EncDotNet.S100.Renderers.Mapsui/Diagnostics/Telemetry.cs</c>) are
/// intentionally NOT renamed: they measure the inline radial-distance
/// simplification cache and remain live regardless of whether the LOD
/// pyramid is enabled, so dashboards continue to work.
/// </para>
/// </remarks>
public static class GeometryLodMetrics
{
    /// <summary>
    /// Vertex count of the input line fed into
    /// <see cref="EncDotNet.S100.Pipelines.Vector.Caching.LineLodPyramid.Build"/>.
    /// Combined with <see cref="VerticesOut"/> this gives the reduction
    /// ratio per feature type.
    /// </summary>
    public static readonly Histogram<long> VerticesIn =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.geometry.vertices.in",
            unit: "{vertices}",
            description: "Input vertex count of a line fed into the LOD pyramid builder.");

    /// <summary>
    /// Vertex count of the selected LOD level actually rasterised. Tagged
    /// by feature type and (when known) the pyramid level index.
    /// </summary>
    public static readonly Histogram<long> VerticesOut =
        Telemetry.Meter.CreateHistogram<long>(
            name: "s100.geometry.vertices.out",
            unit: "{vertices}",
            description: "Output vertex count of the LOD level selected for rendering.");

    /// <summary>
    /// Wall-clock duration of a single
    /// <see cref="EncDotNet.S100.Pipelines.Vector.Caching.LineLodPyramid.Build"/>
    /// call — the cost that a disk-cache hit avoids on subsequent runs.
    /// </summary>
    public static readonly Histogram<double> BuildDuration =
        Telemetry.Meter.CreateHistogram<double>(
            name: "s100.geometry.lod.build.duration",
            unit: "ms",
            description: "Wall-clock duration of a LineLodPyramid.Build pass.");

    /// <summary>
    /// Number of line-LOD cache lookups served from the cache (no rebuild).
    /// Incremented from <c>ILineLodCache</c> implementations and, when
    /// consumed by the renderer, from
    /// <c>CachedVectorStyleRenderer</c> as well.
    /// </summary>
    public static readonly Counter<long> CacheHits =
        Telemetry.Meter.CreateCounter<long>(
            name: "s100.geometry.lod.cache.hit.count",
            unit: "{lookups}",
            description: "Line LOD cache lookups served from cache.");

    /// <summary>
    /// Number of line-LOD cache lookups that had to invoke the factory
    /// (miss). See <see cref="CacheHits"/>.
    /// </summary>
    public static readonly Counter<long> CacheMisses =
        Telemetry.Meter.CreateCounter<long>(
            name: "s100.geometry.lod.cache.miss.count",
            unit: "{lookups}",
            description: "Line LOD cache lookups that ran the factory (miss).");
}
