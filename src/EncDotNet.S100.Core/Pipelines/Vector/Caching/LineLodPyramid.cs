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
    /// Builds a pyramid for a line whose downstream consumer will select
    /// levels against a viewport whose ground-resolution is expressed in
    /// EPSG:3857 (Web Mercator) metres per pixel — i.e. every current
    /// caller. The input coordinates are still WGS-84
    /// <see cref="GeoPosition"/> (so the pyramid remains unprojected and
    /// composes with the future reproject-once cache tracked by #488), but
    /// the tolerance ladder is interpreted as Mercator metres and internally
    /// scaled by <c>cos(featureMidLatitude)</c> before Douglas-Peucker is
    /// applied in the equirectangular real-metre frame. Each returned level
    /// then records the <em>original</em> Mercator-equivalent tolerance so
    /// <see cref="SelectLevelIndex"/> can compare a Mercator budget to a
    /// Mercator tolerance apples-to-apples.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Web Mercator inflates linear distance by
    /// <c>1/cos φ</c>, so a Cartesian DP applied in EPSG:3857 with tolerance
    /// <c>T</c> keeps points whose real perpendicular deviation exceeds
    /// <c>T·cos φ</c>. Applying an equirectangular real-metre DP with the
    /// same <c>T</c> would instead keep points whose real deviation exceeds
    /// <c>T</c>, dropping more vertices than the Cartesian path and
    /// silently violating the sub-pixel-on-screen guarantee at
    /// non-equatorial latitudes (the higher the latitude, the larger the
    /// gap: 1.4× at 45°N, 1.7× at 54°N, 2× at 60°N). This overload closes
    /// that gap so the flag-on renderer path is visually parity-equivalent
    /// with the pre-#489 (PR-2) Cartesian pyramid.
    /// </para>
    /// <para>
    /// <b>Residual.</b> The equirectangular frame anchors at a single
    /// mid-latitude, whereas real Mercator scale varies continuously along
    /// a feature that spans a latitude range. For typical S-101 line
    /// features (edge geometries, contours, coastline segments) the span is
    /// small enough that the residual is well under a pixel; for very long
    /// features that cross more than a degree of latitude the residual can
    /// become measurable but is still bounded by the second-order cos-Taylor
    /// term.
    /// </para>
    /// </remarks>
    /// <param name="coordinates">
    /// The input line's vertices in lat/lon order. Must have at least two
    /// points; degenerate inputs are returned as a single passthrough level.
    /// </param>
    /// <param name="mercatorTolerancesMetres">
    /// Simplification tolerances in <b>Mercator metres</b>, largest first —
    /// the same numbers a Cartesian DP over already-projected EPSG:3857
    /// coordinates would consume (typically
    /// <see cref="LineLodTolerances.HalfOctaveDefault"/>). Values must be
    /// positive and strictly descending.
    /// </param>
    /// <returns>
    /// A pyramid with <c>mercatorTolerancesMetres.Count + 1</c> levels: one
    /// per requested tolerance (with <c>Level.ToleranceMetres</c> recording
    /// the original Mercator number), then a passthrough level carrying the
    /// input coordinates unchanged.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="mercatorTolerancesMetres"/> is empty or not strictly
    /// descending, or contains a non-positive value.
    /// </exception>
    public static LineLodPyramid BuildForMercatorSelection(
        IReadOnlyList<GeoPosition> coordinates,
        IReadOnlyList<double> mercatorTolerancesMetres)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(mercatorTolerancesMetres);

        if (mercatorTolerancesMetres.Count == 0)
        {
            throw new ArgumentException(
                "At least one tolerance is required.", nameof(mercatorTolerancesMetres));
        }

        for (var i = 0; i < mercatorTolerancesMetres.Count; i++)
        {
            if (mercatorTolerancesMetres[i] <= 0)
            {
                throw new ArgumentException(
                    "Tolerances must be positive.", nameof(mercatorTolerancesMetres));
            }

            if (i > 0 && mercatorTolerancesMetres[i] >= mercatorTolerancesMetres[i - 1])
            {
                throw new ArgumentException(
                    "Tolerances must be strictly descending (coarsest first).",
                    nameof(mercatorTolerancesMetres));
            }
        }

        if (coordinates.Count < 3)
        {
            GeometryLodMetrics.VerticesIn.Record(coordinates.Count);
            return new LineLodPyramid(
                [LineLodLevel.CreatePassthrough(coordinates)],
                coordinates.Count);
        }

        // Compute the feature's mid-latitude in radians once; the DP frame
        // is equirectangular anchored here, so cos(midLat) is the correct
        // one-shot scaling factor for the whole feature.
        var minLat = coordinates[0].Latitude;
        var maxLat = minLat;
        for (var i = 1; i < coordinates.Count; i++)
        {
            var lat = coordinates[i].Latitude;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
        }
        var midLatRadians = (minLat + maxLat) * 0.5 * (Math.PI / 180.0);
        var cosMidLat = Math.Cos(midLatRadians);

        // Defensive: cos is 0 exactly at the poles. Fall back to the
        // real-metre semantics rather than build a pyramid that would
        // collapse to a single vertex.
        if (cosMidLat <= 0.0)
        {
            return Build(coordinates, mercatorTolerancesMetres);
        }

        var buildStopwatch = Stopwatch.StartNew();

        var levels = new List<LineLodLevel>(mercatorTolerancesMetres.Count + 1);
        foreach (var mercatorTolerance in mercatorTolerancesMetres)
        {
            // DP consumes real metres; scale down by cos(midLat) so the
            // dropped-vertex threshold matches what a Cartesian DP with
            // this Mercator tolerance would drop.
            var realTolerance = mercatorTolerance * cosMidLat;
            var simplified = DouglasPeuckerLineSimplifier.Simplify(
                coordinates, realTolerance);
            // Record the ORIGINAL Mercator tolerance so
            // SelectLevelIndex(mercatorBudget) is apples-to-apples.
            levels.Add(new LineLodLevel(mercatorTolerance, simplified, isPassthrough: false));
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
        => Levels[SelectLevelIndex(groundResolutionMetresPerPixel, targetPixels)];

    /// <summary>
    /// Index-returning variant of <see cref="SelectLevel"/>. Callers that
    /// key downstream caches by LOD band (e.g. an SKPath cache in the Mapsui
    /// renderer) use the returned index directly as the cache key instead of
    /// hashing the whole level.
    /// </summary>
    public int SelectLevelIndex(
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
                return i;
            }
        }

        // Should be unreachable because the passthrough level always matches;
        // returning the finest level defensively.
        return Levels.Count - 1;
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

    /// <summary>
    /// Opaque version tag folded into <see cref="ILineLodCache"/> keys so
    /// changing the built-in ladder (or the simplifier algorithm) forces a
    /// cold rebuild of every persisted pyramid rather than serving stale
    /// entries whose input contract has shifted. Bump this string whenever
    /// <see cref="HalfOctaveDefault"/> or
    /// <see cref="DouglasPeuckerLineSimplifier"/> semantics change.
    /// </summary>
    /// <remarks>
    /// <b>v2 (this repo, post-#489 tolerance-unit fix):</b> the S-101
    /// pre-build now invokes
    /// <see cref="LineLodPyramid.BuildForMercatorSelection"/>, which
    /// scales the ladder by <c>cos(featureMidLatitude)</c> so DP output
    /// matches what a Cartesian EPSG:3857 DP would drop for the same
    /// Mercator tolerance. On-disk pyramids from v1 (which applied the
    /// ladder directly as real metres) would produce coarser vertex sets
    /// than the renderer's fallback path expects; the version bump forces
    /// a cold rebuild rather than serving stale entries.
    /// </remarks>
    public const string ToleranceLadderVersion = "v2-half-octave-256-64-16-mercator-equiv";
}
