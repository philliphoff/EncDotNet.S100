using System.Diagnostics;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// A small pyramid of resolution-appropriate simplifications of one line
/// feature's geometry — the precomputed generalisation product described in
/// issue #489. Each level carries the same line coarsened to a specific
/// screen-pixel tolerance so the renderer can pick the coarsest level whose
/// tolerance is still sub-pixel at the current viewport ground resolution.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coordinate space is deliberately unprojected.</b> Coordinates are stored
/// as <see cref="GeoPosition"/> (WGS-84 lat/lon), never projected to
/// EPSG:3857. This lets the pyramid compose with any downstream
/// projection cache (e.g. the reproject-once work tracked in issue #488)
/// without duplicating projected copies per LOD level. A downstream projection
/// step transforms the picked level once at scene-build time, in the same
/// place it transforms full-resolution geometry today.
/// </para>
/// <para>
/// <b>Not for polygons.</b> The perf A/B in
/// <c>docs/design/mapsui-performance.md</c> demonstrated that polygon
/// simplification yields no paint benefit (warm paints are cache-served
/// through the translation-invariant <c>SKPath</c> cache) and is regressive
/// under multi-cell cache pressure. Only line features get pyramids.
/// </para>
/// <para>
/// <b>Level ordering.</b> Level 0 is the <em>coarsest</em> (largest tolerance,
/// fewest vertices). Higher levels are progressively finer. The last
/// element is the passthrough "vertex-exact" band whose tolerance is
/// <c>0 m</c>: it stores the input coordinates unchanged so pyramids can
/// serve zoom levels finer than the finest LOD without a shape change.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var pyramid = LineLodPyramid.Build(inputCoords, LineLodTolerances.HalfOctaveDefault);
/// // Pick the level whose tolerance is still sub-pixel at this viewport.
/// var level = pyramid.SelectLevel(groundResolutionMetresPerPixel: 42.0);
/// var simplified = level.Coordinates; // fewer points, same shape
/// </code>
/// </example>
public sealed class LineLodPyramid
{
    /// <summary>
    /// Ordered levels, coarsest first. The final element is the passthrough
    /// vertex-exact level (<see cref="LineLodLevel.IsPassthrough"/>).
    /// </summary>
    public IReadOnlyList<LineLodLevel> Levels { get; }

    /// <summary>
    /// Total vertex count across the input passed to <see cref="Build"/>.
    /// Recorded so telemetry can attribute vertex-reduction ratios without
    /// re-inspecting the source geometry.
    /// </summary>
    public int InputVertexCount { get; }

    internal LineLodPyramid(IReadOnlyList<LineLodLevel> levels, int inputVertexCount)
    {
        Levels = levels;
        InputVertexCount = inputVertexCount;
    }

    /// <summary>
    /// Builds a pyramid for one line: one <see cref="LineLodLevel"/> per
    /// tolerance in <paramref name="tolerancesMetres"/> (coarsest first),
    /// plus a passthrough level appended at the end. Each level is produced
    /// by <see cref="DouglasPeuckerLineSimplifier"/> at the given tolerance
    /// expressed in metres on the WGS-84 ellipsoid at the input's mid-latitude
    /// (an equirectangular approximation that is accurate to well under a
    /// pixel at mid-latitudes and only used to pick the coarser LOD level).
    /// </summary>
    /// <param name="coordinates">
    /// The input line's vertices in lat/lon order. Must have at least two
    /// points; degenerate inputs are returned as a single passthrough level.
    /// </param>
    /// <param name="tolerancesMetres">
    /// Simplification tolerances in metres, largest first. Values must be
    /// positive and strictly descending. Callers typically pass
    /// <see cref="LineLodTolerances.HalfOctaveDefault"/>.
    /// </param>
    /// <returns>
    /// A pyramid with <c>tolerancesMetres.Length + 1</c> levels: one per
    /// requested tolerance, then a passthrough level carrying the input
    /// coordinates unchanged.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="tolerancesMetres"/> is empty or not strictly
    /// descending, or contains a non-positive value.
    /// </exception>
    public static LineLodPyramid Build(
        IReadOnlyList<GeoPosition> coordinates,
        IReadOnlyList<double> tolerancesMetres)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(tolerancesMetres);

        if (tolerancesMetres.Count == 0)
        {
            throw new ArgumentException(
                "At least one tolerance is required.", nameof(tolerancesMetres));
        }

        for (var i = 0; i < tolerancesMetres.Count; i++)
        {
            if (tolerancesMetres[i] <= 0)
            {
                throw new ArgumentException(
                    "Tolerances must be positive.", nameof(tolerancesMetres));
            }

            if (i > 0 && tolerancesMetres[i] >= tolerancesMetres[i - 1])
            {
                throw new ArgumentException(
                    "Tolerances must be strictly descending (coarsest first).",
                    nameof(tolerancesMetres));
            }
        }

        // Passthrough for degenerate inputs — nothing to simplify.
        if (coordinates.Count < 3)
        {
            GeometryLodMetrics.VerticesIn.Record(coordinates.Count);
            return new LineLodPyramid(
                [LineLodLevel.CreatePassthrough(coordinates)],
                coordinates.Count);
        }

        var buildStopwatch = Stopwatch.StartNew();

        var levels = new List<LineLodLevel>(tolerancesMetres.Count + 1);
        foreach (var toleranceMetres in tolerancesMetres)
        {
            var simplified = DouglasPeuckerLineSimplifier.Simplify(
                coordinates, toleranceMetres);
            levels.Add(new LineLodLevel(toleranceMetres, simplified, isPassthrough: false));
        }

        levels.Add(LineLodLevel.CreatePassthrough(coordinates));

        buildStopwatch.Stop();
        GeometryLodMetrics.VerticesIn.Record(coordinates.Count);
        GeometryLodMetrics.BuildDuration.Record(buildStopwatch.Elapsed.TotalMilliseconds);

        return new LineLodPyramid(levels, coordinates.Count);
    }

    /// <summary>
    /// Selects the coarsest level whose tolerance is at or below
    /// <paramref name="groundResolutionMetresPerPixel"/> multiplied by
    /// <paramref name="targetPixels"/> — i.e. the level whose dropped detail
    /// is guaranteed sub-pixel on screen.
    /// </summary>
    /// <param name="groundResolutionMetresPerPixel">
    /// Ground resolution at the viewport (metres per screen pixel). Must be
    /// positive. At EPSG:3857, Mapsui's <c>Viewport.Resolution</c> is this
    /// value (adjusted for latitude by the projection).
    /// </param>
    /// <param name="targetPixels">
    /// Pixel budget for dropped detail. Defaults to 0.5 (half a pixel),
    /// mirroring the perf-doc guidance that a ½–1 px tolerance is
    /// conservative enough for thick strokes such as depth contours.
    /// </param>
    /// <returns>
    /// The selected level. Falls back to the finest level when the viewport
    /// is finer than any bucketed tolerance (so the passthrough level is
    /// returned above the finest LOD, which is the correct sub-pixel-
    /// preservation behaviour).
    /// </returns>
    public LineLodLevel SelectLevel(
        double groundResolutionMetresPerPixel,
        double targetPixels = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            groundResolutionMetresPerPixel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetPixels);

        var budget = groundResolutionMetresPerPixel * targetPixels;

        // Levels are coarsest first. Pick the first (coarsest) whose
        // tolerance is at or below the budget.
        for (var i = 0; i < Levels.Count; i++)
        {
            var level = Levels[i];
            if (level.IsPassthrough || level.ToleranceMetres <= budget)
            {
                return level;
            }
        }

        // Should be unreachable because the passthrough level always matches;
        // returning the finest level defensively.
        return Levels[^1];
    }
}

/// <summary>
/// One level in a <see cref="LineLodPyramid"/>: a coarser (or passthrough)
/// view of a line's coordinates paired with the tolerance that produced it.
/// </summary>
public sealed class LineLodLevel
{
    /// <summary>
    /// Simplification tolerance in metres that produced <see cref="Coordinates"/>.
    /// A value of <c>0</c> marks this as the <see cref="IsPassthrough"/>
    /// vertex-exact level.
    /// </summary>
    public double ToleranceMetres { get; }

    /// <summary>
    /// Simplified line vertices in the same order as the input. For a
    /// passthrough level these are the input coordinates unchanged.
    /// </summary>
    public IReadOnlyList<GeoPosition> Coordinates { get; }

    /// <summary>
    /// <see langword="true"/> for the vertex-exact passthrough level (no
    /// simplification applied). Renderers use this signal to skip any
    /// downstream inline simplification: the pyramid already promises that
    /// dropped detail is sub-pixel.
    /// </summary>
    public bool IsPassthrough { get; }

    internal LineLodLevel(
        double toleranceMetres,
        IReadOnlyList<GeoPosition> coordinates,
        bool isPassthrough)
    {
        ToleranceMetres = toleranceMetres;
        Coordinates = coordinates;
        IsPassthrough = isPassthrough;
    }

    internal static LineLodLevel CreatePassthrough(IReadOnlyList<GeoPosition> coordinates)
        => new(toleranceMetres: 0.0, coordinates, isPassthrough: true);
}

/// <summary>
/// Default tolerance ladders for building <see cref="LineLodPyramid"/>
/// instances. Values are half-octave-spaced (~2× apart) and chosen so each
/// step covers roughly one zoom-in doubling of the viewport, matching the
/// half-octave bucketing pattern already used by
/// <c>CachedVectorStyleRenderer</c>.
/// </summary>
public static class LineLodTolerances
{
    /// <summary>
    /// Three half-octave-spaced tolerances in metres suitable for global
    /// scales from overview to harbour: <c>256, 64, 16</c>. Chosen so that
    /// at ½-pixel budget they engage roughly at Web-Mercator zoom levels
    /// 8, 10, and 12 respectively (metres-per-pixel doubles per zoom).
    /// The pyramid <see cref="LineLodPyramid.Build"/> appends a passthrough
    /// level after these three, so viewports above zoom ~13 render vertex-
    /// exact — matching today's behaviour above the finest LOD band.
    /// </summary>
    public static IReadOnlyList<double> HalfOctaveDefault { get; } = [256.0, 64.0, 16.0];
}
