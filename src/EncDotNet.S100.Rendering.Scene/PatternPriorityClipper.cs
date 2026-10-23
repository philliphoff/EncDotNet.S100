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
    /// supplied non-patterned colour fills.
    /// </summary>
    /// <param name="entries">
    /// The pattern groups, <b>pre-sorted ascending by priority</b>. The returned
    /// list is in the same order as this input.
    /// </param>
    /// <param name="nonPatternedColorFills">
    /// Non-patterned colour-fill polygons (e.g. land) that should occlude every
    /// pattern fill. The clipper treats each polygon as a full occluder; the
    /// caller is responsible for supplying only fills that are actually opaque.
    /// May be empty.
    /// </param>
    /// <returns>
    /// The clipped geometry for each input group, in input order. A group whose
    /// area is fully occluded yields an empty geometry.
    /// </returns>
    /// <remarks>
    /// <para>The overlay favours robustness: when an individual <c>Difference</c>
    /// or <c>Union</c> fails (a <see cref="TopologyException"/> or
    /// <see cref="ArgumentException"/> on degenerate input, or the
    /// <see cref="AggregateException"/> that <see cref="OverlayNGRobust"/> throws
    /// when all of its fallback strategies fail), the algorithm
    /// falls back to the unclipped geometry for that step rather than failing the
    /// whole clip. This keeps a pattern visible (un-clipped) in preference to
    /// dropping it, matching the historical Mapsui behaviour.</para>
    /// <para>Every overlay result is reduced to its polygonal components before
    /// it is used again or returned. OverlayNG's snapping fallback can collapse a
    /// sliver of an areal result to a line, yielding a mixed-dimension
    /// <see cref="GeometryCollection"/> that a following overlay rejects.</para>
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
            catch (Exception ex) when (IsOverlayFailure(ex))
            {
                // If union fails, skip land clipping
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
                // On failure, fall back to the unclipped geometry.
                clipped = TryOverlay(clipped, higherPriorityAreas, SpatialFunction.Difference) ?? clipped;
            }

            // Subtract non-patterned color fill areas (e.g. land)
            if (excludeAreas is not null &&
                excludeAreas.EnvelopeInternal.Intersects(clipped.EnvelopeInternal))
            {
                // On failure, keep the current clipped geometry.
                clipped = TryOverlay(clipped, excludeAreas, SpatialFunction.Difference) ?? clipped;
            }

            result[i] = new ClippedPattern(patternRef, priority, clipped);

            // Add this entry's (generalized) area to the higher-priority union
            // for use by the next, lower-priority entries. If the union fails,
            // keep the existing accumulated area.
            higherPriorityAreas = higherPriorityAreas is null
                ? geometry
                : TryOverlay(higherPriorityAreas, geometry, SpatialFunction.Union) ?? higherPriorityAreas;
        }

        return result;
    }

    /// <summary>
    /// Runs one robust overlay on the polygonal components of both inputs and
    /// reduces the result to polygonal components too. Returns
    /// <see langword="null"/> when the overlay fails, so the caller can fall back
    /// to its current geometry for that step.
    /// </summary>
    internal static Geometry? TryOverlay(Geometry geom0, Geometry geom1, SpatialFunction opCode)
    {
        Geometry result;
        try
        {
            result = OverlayNGRobust.Overlay(PolygonalOrEmpty(geom0), PolygonalOrEmpty(geom1), opCode);
        }
        catch (Exception ex) when (IsOverlayFailure(ex))
        {
            return null;
        }

        // A result with no polygonal component (an area collapsed to lines or
        // points) has no visible pattern area left.
        return PolygonalOrEmpty(result);
    }

    private static Geometry PolygonalOrEmpty(Geometry geometry) =>
        ExtractPolygonal(geometry) ?? geometry.Factory.CreatePolygon();

    /// <summary>
    /// Whether <paramref name="ex"/> is a NetTopologySuite overlay failure on
    /// degenerate input: a <see cref="TopologyException"/>, an
    /// <see cref="ArgumentException"/> (for example "Overlay input is
    /// mixed-dimension"), or the <see cref="AggregateException"/> that
    /// <see cref="OverlayNGRobust"/> throws when every fallback strategy fails.
    /// </summary>
    internal static bool IsOverlayFailure(Exception ex) => ex switch
    {
        TopologyException or ArgumentException => true,
        AggregateException aggregate => aggregate.InnerExceptions.All(IsOverlayFailure),
        _ => false,
    };

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
        catch (ArgumentException)
        {
            // The simplifier keys its rings by value, so a geometry holding two
            // identical rings (e.g. a duplicated polygon) fails with a duplicate
            // key. Simplifying is only an optimization: clip it as it is.
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
