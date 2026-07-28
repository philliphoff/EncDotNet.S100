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
    /// Douglas–Peucker runs against the vertices <em>projected to true
    /// EPSG:3857 metres</em> — bit-identical to the projection the renderer
    /// applies at draw time via <c>Mapsui.Projections.SphericalMercator.FromLonLat</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why projected DP, not equirect-with-cos-scaling.</b> An earlier
    /// version of this method ran DP in an equirectangular real-metre frame
    /// with the ladder scaled by <c>cos(featureMidLatitude)</c>. That kept
    /// the DP threshold isotropically consistent with the renderer's
    /// Mercator ladder at the feature's mid-latitude, but the two DP paths
    /// still tie-broke "farthest-point" picks differently at sine-apex-
    /// adjacent vertices (a floating-point residual measured at ~one input-
    /// vertex spacing on bounded S-101 features, and up to 1.3 km on
    /// 1°-latitude-span stress geometry). Projecting to true Web Mercator
    /// before DP eliminates the residual: this method now executes the same
    /// two operations, in the same order, as the renderer's pre-#489 inline
    /// Cartesian pyramid — <c>SphericalMercator.FromLonLat</c> then DP at
    /// tolerance <c>T</c> Mercator metres — so the kept vertex set is
    /// bit-identical to the Cartesian pyramid the renderer would build at
    /// draw time.
    /// </para>
    /// <para>
    /// <b>Storage is still unprojected.</b> The projected coordinates are
    /// used only for the DP selection; the kept vertices are then extracted
    /// from the original <see cref="GeoPosition"/> input, so on-disk
    /// pyramids remain lat/lon and compose with the future reproject-once
    /// cache tracked by #488.
    /// </para>
    /// <para>
    /// <b>Pole safety.</b> Web Mercator diverges as latitude approaches
    /// ±90°. Any input latitude beyond the standard EPSG:3857 clip
    /// (±85.05112878°) falls back to <see cref="Build"/> — accepting the
    /// equirectangular residual there rather than emitting infinities. This
    /// bound is well outside S-101 chart coverage.
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
    /// the Mercator-metre number), then a passthrough level carrying the
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

        // Pole safety: Web Mercator diverges near ±90°. Bail to the
        // equirect real-metre path rather than emit infinities. The
        // standard EPSG:3857 clip is ±85.05112878°; anything at or beyond
        // that is well outside S-101 chart coverage.
        for (var i = 0; i < coordinates.Count; i++)
        {
            var lat = coordinates[i].Latitude;
            if (lat >= WebMercatorLatitudeLimit || lat <= -WebMercatorLatitudeLimit)
            {
                return Build(coordinates, mercatorTolerancesMetres);
            }
        }

        // Project once to Web Mercator EPSG:3857 metres. Formula matches
        // Mapsui.Projections.SphericalMercator.FromLonLat byte-for-byte so
        // the DP that runs against these coordinates picks the same vertex
        // set the renderer's pre-#489 inline Cartesian pyramid picks.
        var projected = new (double X, double Y)[coordinates.Count];
        for (var i = 0; i < coordinates.Count; i++)
        {
            var lonRadians = WebMercatorD2R * coordinates[i].Longitude;
            var latRadians = WebMercatorD2R * coordinates[i].Latitude;
            var x = WebMercatorRadius * lonRadians;
            var y = WebMercatorRadius * Math.Log(Math.Tan((Math.PI * 0.25) + (latRadians * 0.5)));
            projected[i] = (x, y);
        }

        var buildStopwatch = Stopwatch.StartNew();

        var levels = new List<LineLodLevel>(mercatorTolerancesMetres.Count + 1);
        foreach (var mercatorTolerance in mercatorTolerancesMetres)
        {
            var keep = DouglasPeuckerLineSimplifier.ComputeKeepMask(
                projected, mercatorTolerance);
            var kept = new List<GeoPosition>(coordinates.Count);
            for (var i = 0; i < coordinates.Count; i++)
            {
                if (keep[i])
                {
                    kept.Add(coordinates[i]);
                }
            }
            levels.Add(new LineLodLevel(mercatorTolerance, kept, isPassthrough: false));
        }

        levels.Add(LineLodLevel.CreatePassthrough(coordinates));

        buildStopwatch.Stop();
        GeometryLodMetrics.VerticesIn.Record(coordinates.Count);
        GeometryLodMetrics.BuildDuration.Record(buildStopwatch.Elapsed.TotalMilliseconds);

        return new LineLodPyramid(levels, coordinates.Count);
    }

    /// <summary>
    /// WGS-84 semi-major axis (metres) used by
    /// <c>Mapsui.Projections.SphericalMercator.FromLonLat</c>. Reproduced
    /// here so <see cref="BuildForMercatorSelection"/> can project without
    /// taking a compile-time dependency on the renderer's Mapsui package,
    /// but the constant must stay in lock-step with Mapsui's value —
    /// otherwise the "bit-identical to the renderer" guarantee is broken.
    /// </summary>
    private const double WebMercatorRadius = 6_378_137.0;

    /// <summary>
    /// Degrees-to-radians constant matching
    /// <c>Mapsui.Projections.SphericalMercator</c>'s <c>D2R</c>.
    /// </summary>
    private const double WebMercatorD2R = Math.PI / 180.0;

    /// <summary>
    /// Standard EPSG:3857 latitude clip beyond which
    /// <c>ln(tan(π/4 + φ/2))</c> diverges to infinity. Values outside
    /// <c>±</c> this bound fall back to the equirectangular real-metre
    /// simplifier.
    /// </summary>
    private const double WebMercatorLatitudeLimit = 85.05112878;

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
    /// <b>v3 (this repo, post-#489 Mercator-DP fix):</b> the S-101 pre-build
    /// projects each vertex with
    /// <c>Mapsui.Projections.SphericalMercator.FromLonLat</c>-equivalent
    /// math and runs Douglas-Peucker directly in Web Mercator metres — the
    /// same two operations, in the same order, the renderer's pre-#489
    /// inline Cartesian pyramid did at draw time. This produces bit-identical
    /// kept-vertex sets. On-disk pyramids from v1 (equirectangular real-metre
    /// DP) and v2 (equirectangular with <c>×cos φ</c> scaled ladder) would
    /// each produce visibly different vertex sets from the renderer's
    /// fallback path; the version bump forces a cold rebuild rather than
    /// serving stale entries.
    /// </remarks>
    public const string ToleranceLadderVersion = "v3-half-octave-256-64-16-mercator-dp";
}
