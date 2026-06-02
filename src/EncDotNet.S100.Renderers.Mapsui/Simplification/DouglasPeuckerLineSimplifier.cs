using System;
using System.Diagnostics;
using Mapsui;
using Mapsui.Nts;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace EncDotNet.S100.Renderers.Mapsui.Simplification;

/// <summary>
/// <see cref="IFeatureSimplifier"/> implementation that runs
/// Douglas-Peucker on <c>LineString</c> and <c>MultiLineString</c>
/// geometries via NTS' <see cref="DouglasPeuckerSimplifier"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the v1 simplifier introduced for issue #164 to address
/// the May/June 2026 perf review's primary finding: ~93% of paint
/// time is consumed by Mapsui's <c>VectorStyleRenderer</c>, scaling
/// linearly with vertex count at ~1&#160;µs/point.
/// </para>
/// <para>
/// Polygons, points, and other geometry types pass through
/// unchanged. Polygon simplification with per-ring DP can produce
/// invalid / self-intersecting rings, so it is deferred to a later
/// pass that wires in <see cref="TopologyPreservingSimplifier"/> +
/// <c>IsValid</c> validation.
/// </para>
/// <para>
/// On a degenerate result (line collapsed to fewer than two points,
/// or simplification did not actually reduce the vertex count) the
/// original feature is returned and <see cref="SimplificationResult.WasSimplified"/>
/// is <see langword="false"/>.
/// </para>
/// </remarks>
public sealed class DouglasPeuckerLineSimplifier : IFeatureSimplifier
{
    /// <summary>Shared, stateless singleton.</summary>
    public static DouglasPeuckerLineSimplifier Instance { get; } = new();

    /// <inheritdoc />
    public SimplificationResult Simplify(
        IFeature feature,
        double toleranceMetres,
        SimplificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(options);

        if (feature is not GeometryFeature gf || gf.Geometry is null)
            return new SimplificationResult(feature, 0, 0, false);

        var geom = gf.Geometry;
        var origCount = CountCoords(geom);

        // Bypass non-line geometry and short geometries up front: no
        // need to invoke NTS for features whose paint cost is already
        // negligible per the perf review.
        if (geom is not (LineString or MultiLineString))
            return new SimplificationResult(feature, origCount, origCount, false);

        if (origCount < options.MinVertexCount)
            return new SimplificationResult(feature, origCount, origCount, false);

        if (!(toleranceMetres > 0))
            return new SimplificationResult(feature, origCount, origCount, false);

        Geometry? simplified;
        try
        {
            simplified = DouglasPeuckerSimplifier.Simplify(geom, toleranceMetres);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Defensive: NTS does not normally throw on valid inputs,
            // but we never want a malformed coordinate sequence to
            // crash the render thread. Fall back to original.
            Debug.WriteLine($"[Simplification] DP threw for {origCount}-coord geom: {ex.Message}");
            return new SimplificationResult(feature, origCount, origCount, false);
        }

        if (simplified is null || simplified.IsEmpty)
            return new SimplificationResult(feature, origCount, origCount, false);

        var newCount = CountCoords(simplified);

        // A line collapsed to a single point or no progress made:
        // serve the original.
        if (newCount < 2 || newCount >= origCount)
            return new SimplificationResult(feature, origCount, origCount, false);

        // Build a sibling feature: same Fields, same Styles instances
        // (shared by reference via GeometryFeature's copy ctor) and a
        // back-reference to the original so picking can resolve through
        // even if Mapsui ends up hit-testing the simplified shape.
        var clone = new GeometryFeature(gf) { Geometry = simplified };
        clone[Simplification.OriginalFeatureKey] = feature;

        return new SimplificationResult(clone, origCount, newCount, true);
    }

    private static long CountCoords(Geometry geom) => geom.NumPoints;
}
