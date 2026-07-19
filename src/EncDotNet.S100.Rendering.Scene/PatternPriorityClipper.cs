using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Simplify;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Backend-neutral priority-clipping of S-100 Part 9 §11.3 tiled pattern area
/// fills. Given the pattern areas grouped by (pattern reference, drawing
/// priority) and the opaque non-patterned colour fills, this computes the
/// visible portion of each pattern group by subtracting every higher-priority
/// pattern area and every opaque colour fill (e.g. land) from it, so that a
/// lower-priority pattern does not show through a higher-priority pattern zone
/// and no pattern bleeds over an opaque area.
/// </summary>
/// <remarks>
/// <para>This is the single, shared implementation used by both render paths:
/// the Mapsui feature path (<c>MapsuiDisplayListRenderer</c>) invokes it
/// directly, and the <see cref="VectorScene"/> IR path
/// (<see cref="VectorSceneBuilder"/>, consumed by the headless Skia backend and
/// the Mapsui TiledScene subsystem) applies it when lowering pattern ops. Both
/// therefore clip identically.</para>
/// <para>All geometry is expressed in EPSG:3857 (Web-Mercator) metres, matching
/// the <see cref="PaintOp"/> unit contract. The algorithm uses NetTopologySuite
/// <see cref="OverlayNGRobust"/> (OverlayNG) for full-precision, robust overlay
/// and generalizes dense areas via <see cref="TopologyPreservingSimplifier"/>
/// (gated by <see cref="MinPointsToSimplify"/>) to keep the overlay cost bounded
/// on very dense coverage cells. The clip geometry is palette-independent, so
/// results are cacheable keyed on the palette-independent portrayal key.</para>
/// </remarks>
public static class PatternPriorityClipper
{
    /// <summary>
    /// A group of same-pattern, same-priority area fills to be clipped as one
    /// unit. Grouping mirrors the Mapsui feature path so both render arms
    /// produce identical clip topology.
    /// </summary>
    /// <param name="PatternRef">The portrayal-catalogue area-fill name.</param>
    /// <param name="Priority">The S-100 Part 9 drawing priority shared by the group.</param>
    /// <param name="Polygons">The pattern-area polygons in EPSG:3857 metres.</param>
    public readonly record struct PatternGroup(
        string PatternRef, int Priority, IReadOnlyList<Polygon> Polygons);

    /// <summary>
    /// The clipped, still-visible geometry for one <see cref="PatternGroup"/>.
    /// </summary>
    /// <param name="PatternRef">The portrayal-catalogue area-fill name.</param>
    /// <param name="Priority">The group's drawing priority.</param>
    /// <param name="Geometry">
    /// The visible portion after clipping; may be an empty geometry, a
    /// <see cref="Polygon"/>, or a <see cref="MultiPolygon"/>.
    /// </param>
    public readonly record struct ClippedPattern(
        string PatternRef, int Priority, Geometry Geometry);

    /// <summary>
    /// Tolerance, in EPSG:3857 (Web Mercator) metres, used to generalize pattern
    /// and exclusion geometries before clipping. Web Mercator inflates distances by
    /// 1/cos(latitude), so this projected tolerance is conservative (smaller in
    /// ground metres) away from the equator. The clipped boundary only bounds a
    /// tiled raster pattern fill, so this generalization is not visually significant.
    /// </summary>
    public const double SimplifyToleranceMetres = 1.0;

    /// <summary>
    /// Minimum vertex count at which <see cref="SimplifyForClip"/> generalizes a
    /// geometry before the clip overlay. Below this, the NetTopologySuite
    /// <c>Difference</c>/<c>Union</c> cost is already small (profiling: a
    /// ~2,600-vertex pattern area clips in ~50&#160;ms) and the simplifier's own
    /// fixed setup cost would be net overhead. The cost the optimization targets is
    /// super-linear and only becomes significant for very dense areas (profiling: a
    /// ~64,000-vertex M_QUAL coverage area took ~7.7&#160;s), so gating on vertex
    /// count applies the generalization only where it provides a clear net win and
    /// leaves the common case (small/moderate areas) byte-identical to no
    /// generalization at all.
    /// </summary>
    public const int MinPointsToSimplify = 2000;

    /// <summary>
    /// Clips each pattern group against all higher-priority pattern areas and the
    /// supplied opaque non-patterned colour fills.
    /// </summary>
    /// <param name="entries">
    /// The pattern groups, <b>pre-sorted ascending by priority</b>. The returned
    /// list is in the same order as this input.
    /// </param>
    /// <param name="nonPatternedColorFills">
    /// Opaque non-patterned colour-fill polygons (e.g. land) that occlude every
    /// pattern fill. May be empty.
    /// </param>
    /// <returns>
    /// The clipped geometry for each input group, in input order. A group whose
    /// area is fully occluded yields an empty geometry.
    /// </returns>
    /// <remarks>
    /// The overlay favours robustness: when an individual <c>Difference</c> or
    /// <c>Union</c> throws (<see cref="TopologyException"/> on degenerate input,
    /// or <see cref="ArgumentException"/> when an accumulated union degenerates to
    /// a <see cref="GeometryCollection"/> that NTS overlay rejects), the algorithm
    /// falls back to the unclipped geometry for that step rather than failing the
    /// whole clip. This keeps a pattern visible (un-clipped) in preference to
    /// dropping it, matching the historical Mapsui behaviour.
    /// </remarks>
    public static IReadOnlyList<ClippedPattern> Clip(
        IReadOnlyList<PatternGroup> entries,
        IReadOnlyList<Polygon> nonPatternedColorFills)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(nonPatternedColorFills);

        if (entries.Count == 0)
            return [];

        // Build a union of non-patterned color fill areas (e.g. land) that
        // should occlude all pattern fills.
        Geometry? excludeAreas = null;
        if (nonPatternedColorFills.Count > 0)
        {
            try
            {
                Geometry nonPatterned = nonPatternedColorFills.Count == 1
                    ? nonPatternedColorFills[0]
                    : new MultiPolygon([.. nonPatternedColorFills]);
                // Reduce to polygonal-only so this never becomes a mixed-dimension
                // overlay clip (see ExtractPolygonal): OverlayNG rejects such inputs.
                excludeAreas = ExtractPolygonal(SimplifyForClip(OverlayNGRobust.Union(nonPatterned)));
            }
            catch (TopologyException)
            {
                // If union fails, skip land clipping
            }
            catch (ArgumentException)
            {
                // Mixed-dimension union input rejected by OverlayNG; skip land clipping.
            }
        }

        // Build merged, generalized geometry for each entry. Simplifying once up
        // front means the (potentially huge) geometry is cheap to use both as a
        // Difference subject and when accumulated into the higher-priority union.
        var merged = entries.Select(e =>
        {
            Geometry g = e.Polygons.Count == 1
                ? e.Polygons[0]
                : new MultiPolygon([.. e.Polygons]);
            return (e.PatternRef, e.Priority, Geometry: SimplifyForClip(g));
        }).ToList();

        // Walk from highest priority down, accumulating a union of
        // higher-priority areas that will clip lower-priority patterns.
        Geometry? higherPriorityAreas = null;
        var result = new ClippedPattern[merged.Count];

        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var (patternRef, priority, geometry) = merged[i];

            // Start with the original geometry, then subtract exclusion areas
            var clipped = geometry;

            // Subtract higher-priority pattern areas (only when they actually
            // overlap this entry's extent — an envelope test avoids a costly
            // overlay when the areas are disjoint).
            if (higherPriorityAreas is not null &&
                higherPriorityAreas.EnvelopeInternal.Intersects(geometry.EnvelopeInternal))
            {
                try
                {
                    clipped = OverlayNGRobust.Overlay(
                        clipped, higherPriorityAreas, SpatialFunction.Difference);
                }
                catch (TopologyException)
                {
                    // Fall back to unclipped geometry
                }
                catch (ArgumentException)
                {
                    // NTS Difference rejects GeometryCollection arguments;
                    // accumulated unions can degenerate to that shape.
                    // Fall back to unclipped geometry.
                }
            }

            // Subtract non-patterned color fill areas (e.g. land)
            if (excludeAreas is not null &&
                excludeAreas.EnvelopeInternal.Intersects(clipped.EnvelopeInternal))
            {
                try
                {
                    clipped = OverlayNGRobust.Overlay(
                        clipped, excludeAreas, SpatialFunction.Difference);
                }
                catch (TopologyException)
                {
                    // Fall back to current clipped geometry
                }
                catch (ArgumentException)
                {
                    // GeometryCollection rejected by NTS Difference; keep current geometry.
                }
            }

            result[i] = new ClippedPattern(patternRef, priority, clipped);

            // Add this entry's (generalized) area to the higher-priority union
            // for use by the next, lower-priority entries. The union is reduced
            // to polygonal-only so it can never become a mixed-dimension
            // GeometryCollection that the next iteration's Difference overlay
            // (and OverlayNG) would reject.
            try
            {
                Geometry accumulated = higherPriorityAreas is null
                    ? geometry
                    : OverlayNGRobust.Overlay(higherPriorityAreas, geometry, SpatialFunction.Union);
                higherPriorityAreas = ExtractPolygonal(accumulated) ?? higherPriorityAreas;
            }
            catch (TopologyException)
            {
                // If union fails, keep the existing accumulated area
            }
            catch (ArgumentException)
            {
                // NTS Union rejects GeometryCollection LHS;
                // keep existing accumulated area.
            }
        }

        return result;
    }

    /// <summary>
    /// Generalizes a polygonal geometry for use as a clip subject/mask, preserving
    /// topological validity so the result is safe for subsequent NetTopologySuite
    /// overlay operations. Geometries below <see cref="MinPointsToSimplify"/>
    /// vertices are returned unchanged (the overlay is already inexpensive at that
    /// size). Returns the original geometry if simplification fails or degenerates
    /// to empty.
    /// </summary>
    /// <param name="geometry">The geometry to generalize.</param>
    /// <returns>The generalized (or original) geometry.</returns>
    public static Geometry SimplifyForClip(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.NumPoints < MinPointsToSimplify)
            return geometry;

        try
        {
            var simplified = TopologyPreservingSimplifier.Simplify(
                geometry, SimplifyToleranceMetres);

            if (simplified is null || simplified.IsEmpty)
                return geometry;

            if (!simplified.IsValid)
            {
                var fixedGeometry = simplified.Buffer(0);
                simplified = fixedGeometry.IsEmpty ? geometry : fixedGeometry;
            }

            // Topology-preserving simplification (and the Buffer(0) repair above)
            // can collapse thin polygons to linestrings, producing a
            // mixed-dimension GeometryCollection. OverlayNG rejects such inputs
            // with "Overlay input is mixed-dimension", which previously failed the
            // entire pattern-clip for a cell. Keep only the polygonal components
            // so the result is always a valid overlay subject/clip area.
            var polygonal = ExtractPolygonal(simplified);
            return polygonal is null || polygonal.IsEmpty ? geometry : polygonal;
        }
        catch (TopologyException)
        {
            return geometry;
        }
    }

    /// <summary>
    /// Reduces an arbitrary geometry to its polygonal components, returning a
    /// <see cref="Polygon"/> or <see cref="MultiPolygon"/> (or <see langword="null"/>
    /// when no polygonal component exists). This guards the pattern-clip overlays
    /// against the mixed-dimension <see cref="GeometryCollection"/> that
    /// topology-preserving simplification can emit, which OverlayNG rejects.
    /// </summary>
    /// <param name="geometry">The geometry to reduce; may be any dimension.</param>
    /// <returns>
    /// The polygonal-only geometry, or <see langword="null"/> when the input
    /// contains no polygon.
    /// </returns>
    public static Geometry? ExtractPolygonal(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry is Polygon or MultiPolygon)
            return geometry;

        var polygons = new List<Polygon>();
        CollectPolygons(geometry, polygons);

        if (polygons.Count == 0)
            return null;
        if (polygons.Count == 1)
            return polygons[0];

        return geometry.Factory.CreateMultiPolygon([.. polygons]);
    }

    private static void CollectPolygons(Geometry geometry, List<Polygon> sink)
    {
        switch (geometry)
        {
            case Polygon polygon:
                sink.Add(polygon);
                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                    CollectPolygons(collection.GetGeometryN(i), sink);
                break;
        }
    }
}
