namespace EncDotNet.S100.Renderers.Mapsui.Simplification;

/// <summary>
/// Configuration for resolution-aware geometry simplification.  See
/// <see cref="SimplificationCache"/> and
/// <see cref="DouglasPeuckerLineSimplifier"/> for how each value is
/// consumed.
/// </summary>
/// <param name="PixelTolerance">
/// Maximum displacement, in screen pixels, that simplification is
/// allowed to introduce. Tolerance in metres for a given paint is
/// <c>PixelTolerance × resolution</c> (where <c>resolution</c> is
/// Mapsui's m/px in EPSG:3857). Defaults to <c>0.5</c> — a half pixel
/// — which keeps thicker strokes (depth contours, fairway boundaries)
/// visually identical at typical zoom levels. Set higher (e.g. 1.0)
/// for more aggressive simplification, lower (e.g. 0.25) to be more
/// conservative.
/// </param>
/// <param name="MinVertexCount">
/// Geometries with fewer than this many vertices are returned as-is
/// without invoking the simplifier. Defaults to <c>64</c>: per the
/// May/June 2026 Mapsui perf review, paint cost on geometries with
/// &lt; 100 vertices is negligible (&lt; 7% of total), so the
/// simplifier overhead is not worth paying for them.
/// </param>
/// <param name="MaxCachedCoordinates">
/// Soft upper bound on the total number of coordinates retained in
/// the simplification cache across all buckets. When a bucket
/// transition pushes the cache above this, entries are evicted from
/// the bucket farthest from the current one until the budget is
/// satisfied. Bounded by <em>coordinate count</em> rather than entry
/// count so one 10 000-vertex polyline costs the budget what 10 000
/// small features would. Defaults to <c>5_000_000</c> coords —
/// roughly 80&#160;MB of <c>Coordinate</c> instances on .NET.
/// </param>
public sealed record SimplificationOptions(
    double PixelTolerance = 0.5,
    int MinVertexCount = 64,
    long MaxCachedCoordinates = 5_000_000)
{
    /// <summary>
    /// Default options — what the viewer's
    /// <c>EnableGeometrySimplification</c> setting enables.
    /// </summary>
    public static SimplificationOptions Default { get; } = new();
}
