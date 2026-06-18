using System;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Central, mutable configuration for the Mapsui vector-rendering optimizations
/// surfaced in the viewer's <c>Settings → Map</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Each flag is <i>seeded</i> from the legacy environment variable that used to
/// be the sole control (so the performance A/B harness and headless tooling keep
/// working unchanged), and may then be overridden at runtime by the viewer's
/// settings.
/// </para>
/// <para>
/// When an environment variable is <b>explicitly</b> set (present and non-empty)
/// it takes precedence: the matching <c>…EnvExplicit</c> flag is <see langword="true"/>
/// and subsequent programmatic writes are ignored. This keeps perf measurements
/// faithful — a run started with <c>S100_VECTOR_PICTURE_SNAPSHOT=0</c> stays off
/// regardless of the persisted user setting. Normal users (no env var) get fully
/// live-toggleable knobs.
/// </para>
/// <para>
/// The "best" default for every knob is <see langword="true"/> (geometry
/// simplification at <see cref="DefaultSimplificationTolerancePx"/> px); see
/// <c>docs/design/mapsui-performance.md</c> for the measurements behind that
/// choice.
/// </para>
/// </remarks>
public static class RenderingOptimizations
{
    /// <summary>Default geometry-simplification tolerance, in screen pixels, used when the knob is on and no env override is present.</summary>
    public const double DefaultSimplificationTolerancePx = 0.6;

    private static bool s_vectorSnapshotEnabled;
    private static bool s_vectorSnapshotPrebuildEnabled;
    private static bool s_vectorPathCacheEnabled;
    private static bool s_geometrySimplificationEnabled;
    private static bool s_polygonSimplificationEnabled;

    static RenderingOptimizations()
    {
        (s_vectorSnapshotEnabled, VectorSnapshotEnvExplicit) =
            SeedBool("S100_VECTOR_PICTURE_SNAPSHOT", defaultValue: true);

        (s_vectorSnapshotPrebuildEnabled, VectorSnapshotPrebuildEnvExplicit) =
            SeedBool("S100_VECTOR_SNAPSHOT_PREBUILD", defaultValue: true);

        (s_vectorPathCacheEnabled, VectorPathCacheEnvExplicit) =
            SeedBool("S100_VECTOR_PATH_CACHE", defaultValue: true);

        (s_geometrySimplificationEnabled, SimplificationTolerancePx, GeometrySimplificationEnvExplicit) =
            SeedSimplification();

        (s_polygonSimplificationEnabled, PolygonSimplificationEnvExplicit) =
            SeedBool("S100_VECTOR_POLYGON_SIMPLIFY", defaultValue: false);
    }

    /// <summary>
    /// Whether the raster vector-layer snapshot fast path
    /// (<see cref="S100VectorSnapshotRenderer"/>) is enabled. Records a settled
    /// layer once per (resolution, feature-set) and blits it under translation on
    /// subsequent pans. Default on.
    /// </summary>
    public static bool VectorSnapshotEnabled
    {
        get => s_vectorSnapshotEnabled;
        set { if (!VectorSnapshotEnvExplicit) s_vectorSnapshotEnabled = value; }
    }

    /// <summary>True when <see cref="VectorSnapshotEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool VectorSnapshotEnvExplicit { get; }

    /// <summary>
    /// Whether the off-thread snapshot prebuild is enabled. Hides the one-time
    /// record stall on zoom and the sustained-pan record stall by rasterizing on
    /// a background thread. Only meaningful when <see cref="VectorSnapshotEnabled"/>
    /// is on. Default on.
    /// </summary>
    public static bool VectorSnapshotPrebuildEnabled
    {
        get => s_vectorSnapshotPrebuildEnabled;
        set { if (!VectorSnapshotPrebuildEnvExplicit) s_vectorSnapshotPrebuildEnabled = value; }
    }

    /// <summary>True when <see cref="VectorSnapshotPrebuildEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool VectorSnapshotPrebuildEnvExplicit { get; }

    /// <summary>
    /// Whether the translation-invariant vector path cache
    /// (<see cref="CachedVectorStyleRenderer"/>) is enabled. Builds each
    /// geometry's projected <c>SKPath</c> once per (feature, resolution) and
    /// re-uses it across pans instead of rebuilding every frame. Default on.
    /// </summary>
    public static bool VectorPathCacheEnabled
    {
        get => s_vectorPathCacheEnabled;
        set { if (!VectorPathCacheEnvExplicit) s_vectorPathCacheEnabled = value; }
    }

    /// <summary>True when <see cref="VectorPathCacheEnabled"/> is pinned by an explicit environment variable.</summary>
    public static bool VectorPathCacheEnvExplicit { get; }

    /// <summary>
    /// Whether resolution-aware geometry simplification (dropping on-screen
    /// sub-pixel detail from dense S-101 geometries at path-build time) is
    /// enabled. Applied inside <see cref="CachedVectorStyleRenderer"/> for both
    /// lines (inline sub-pixel vertex dropping) and polygons (topology-preserving
    /// simplification) and therefore requires <see cref="VectorPathCacheEnabled"/>.
    /// Default on.
    /// </summary>
    public static bool GeometrySimplificationEnabled
    {
        get => s_geometrySimplificationEnabled;
        set { if (!GeometrySimplificationEnvExplicit) s_geometrySimplificationEnabled = value; }
    }

    /// <summary>True when geometry simplification is pinned by an explicit <c>S100_VECTOR_SIMPLIFY_PX</c>.</summary>
    public static bool GeometrySimplificationEnvExplicit { get; }

    /// <summary>
    /// Pixel tolerance applied when <see cref="GeometrySimplificationEnabled"/> is
    /// on. Seeded from <c>S100_VECTOR_SIMPLIFY_PX</c>, otherwise
    /// <see cref="DefaultSimplificationTolerancePx"/>. Shared by line and polygon
    /// simplification.
    /// </summary>
    public static double SimplificationTolerancePx { get; }

    /// <summary>
    /// Whether <b>polygon</b> simplification specifically is enabled. Gated in
    /// addition to <see cref="GeometrySimplificationEnabled"/> (polygons are
    /// simplified only when both are on), so polygon simplification can be
    /// toggled in isolation — e.g. via <c>S100_VECTOR_POLYGON_SIMPLIFY=1</c> — for
    /// A/B measurement without affecting line simplification.
    /// </summary>
    /// <remarks>
    /// Default <b>off</b>. Multi-dataset stress measurement (15 S-101 cells,
    /// pan/zoom across boundaries) showed topology-preserving polygon
    /// simplification is net-negative for paint on the GPU path: the
    /// <c>TopologyPreservingSimplifier</c> cost is paid on every path-build and is
    /// not recovered by reduced fill (Metal fill is area-bound, not vertex-bound),
    /// and under cache pressure the cost is re-paid on every rebuild. It is kept
    /// as an opt-in knob for future evaluation (e.g. other data, CPU-bound paths,
    /// or memory-pressure scenarios). See
    /// <c>docs/design/mapsui-performance.md</c>.
    /// </remarks>
    public static bool PolygonSimplificationEnabled
    {
        get => s_polygonSimplificationEnabled;
        set { if (!PolygonSimplificationEnvExplicit) s_polygonSimplificationEnabled = value; }
    }

    /// <summary>True when <see cref="PolygonSimplificationEnabled"/> is pinned by an explicit <c>S100_VECTOR_POLYGON_SIMPLIFY</c>.</summary>
    public static bool PolygonSimplificationEnvExplicit { get; }

    private static (bool value, bool envExplicit) SeedBool(string envName, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrEmpty(raw))
        {
            return (defaultValue, false);
        }

        var enabled = raw is not ("0" or "false" or "FALSE" or "False" or "off" or "OFF");
        return (enabled, true);
    }

    private static (bool enabled, double tolerancePx, bool envExplicit) SeedSimplification()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SIMPLIFY_PX");
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            // A tolerance of 0 (or negative) means "vertex-exact" — i.e. the
            // optimization is explicitly disabled.
            return v > 0 ? (true, v, true) : (false, DefaultSimplificationTolerancePx, true);
        }

        return (true, DefaultSimplificationTolerancePx, false);
    }
}
