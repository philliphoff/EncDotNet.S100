using System.Diagnostics;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines.Vector.Caching;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Cartesian variant of <see cref="LineLodPyramid"/> used inside the Mapsui
/// renderer, where line coordinates arrive already projected to the map CRS
/// (EPSG:3857) in metres. Keeps the same tolerance-ladder concept and level
/// ordering as the Core WGS-84 pyramid but skips the equirectangular
/// re-projection step, which would be redundant given Mapsui's
/// <c>viewport.Resolution</c> is already metres/pixel in the same frame.
/// </summary>
/// <remarks>
/// This lives in the renderer assembly deliberately: the Core pyramid stores
/// unprojected <see cref="EncDotNet.S100.DataModel.GeoPosition"/> so it can
/// compose with the future reproject-once cache tracked by issue #488, and
/// so it survives on disk across restarts without a CRS assumption. The
/// renderer path here uses the pre-projected NTS coordinates it already has;
/// once #488 exposes <c>ProjectedGeometry</c> shapes with WGS-84 pointers to
/// full-resolution coords, this can migrate to the Core type.
/// </remarks>
internal sealed class CartesianLineLodPyramid
{
    /// <summary>Ordered levels, coarsest first; the final element is passthrough.</summary>
    public IReadOnlyList<CartesianLineLodLevel> Levels { get; }

    /// <summary>Input vertex count; recorded for telemetry.</summary>
    public int InputVertexCount { get; }

    internal CartesianLineLodPyramid(IReadOnlyList<CartesianLineLodLevel> levels, int inputVertexCount)
    {
        Levels = levels;
        InputVertexCount = inputVertexCount;
    }

    /// <summary>
    /// Builds a pyramid from projected metric coordinates using
    /// <see cref="LineLodTolerances.HalfOctaveDefault"/> as the tolerance
    /// ladder. Records
    /// <see cref="GeometryLodMetrics.BuildDuration"/> and
    /// <see cref="GeometryLodMetrics.VerticesIn"/> on completion so cold
    /// build cost is observable.
    /// </summary>
    public static CartesianLineLodPyramid Build(
        ReadOnlySpan<CartesianPoint> coordinates,
        IReadOnlyList<double> tolerancesMetres)
    {
        ArgumentNullException.ThrowIfNull(tolerancesMetres);

        if (tolerancesMetres.Count == 0)
        {
            throw new ArgumentException(
                "At least one tolerance is required.", nameof(tolerancesMetres));
        }

        if (coordinates.Length < 3)
        {
            var only = coordinates.ToArray();
            GeometryLodMetrics.VerticesIn.Record(coordinates.Length);
            return new CartesianLineLodPyramid(
                [CartesianLineLodLevel.CreatePassthrough(only)],
                coordinates.Length);
        }

        var stopwatch = Stopwatch.StartNew();

        var levels = new List<CartesianLineLodLevel>(tolerancesMetres.Count + 1);
        foreach (var tolerance in tolerancesMetres)
        {
            var simplified = CartesianDouglasPeucker.Simplify(coordinates, tolerance);
            levels.Add(new CartesianLineLodLevel(tolerance, simplified, isPassthrough: false));
        }
        levels.Add(CartesianLineLodLevel.CreatePassthrough(coordinates.ToArray()));

        stopwatch.Stop();
        GeometryLodMetrics.VerticesIn.Record(coordinates.Length);
        GeometryLodMetrics.BuildDuration.Record(stopwatch.Elapsed.TotalMilliseconds);

        return new CartesianLineLodPyramid(levels, coordinates.Length);
    }

    /// <summary>
    /// Selects the coarsest level whose tolerance is at or below
    /// <paramref name="resolutionMetresPerPixel"/> × <paramref name="targetPixels"/>
    /// — i.e. the level whose dropped detail is sub-pixel on screen.
    /// </summary>
    /// <returns>
    /// The index of the selected level in <see cref="Levels"/>. Falls
    /// through to the passthrough level (last index) when the viewport is
    /// finer than the finest LOD band, matching today's above-band
    /// behaviour.
    /// </returns>
    public int SelectLevelIndex(double resolutionMetresPerPixel, double targetPixels = 0.5)
    {
        var budget = resolutionMetresPerPixel * targetPixels;
        for (var i = 0; i < Levels.Count; i++)
        {
            var level = Levels[i];
            if (level.IsPassthrough || level.ToleranceMetres <= budget)
            {
                return i;
            }
        }
        return Levels.Count - 1;
    }
}

/// <summary>One level in a <see cref="CartesianLineLodPyramid"/>.</summary>
internal sealed class CartesianLineLodLevel
{
    /// <summary>Simplification tolerance in metres. <c>0</c> for passthrough.</summary>
    public double ToleranceMetres { get; }

    /// <summary>Simplified vertices in the same order as the input.</summary>
    public CartesianPoint[] Coordinates { get; }

    /// <summary><see langword="true"/> for the vertex-exact passthrough level.</summary>
    public bool IsPassthrough { get; }

    internal CartesianLineLodLevel(double toleranceMetres, CartesianPoint[] coordinates, bool isPassthrough)
    {
        ToleranceMetres = toleranceMetres;
        Coordinates = coordinates;
        IsPassthrough = isPassthrough;
    }

    internal static CartesianLineLodLevel CreatePassthrough(CartesianPoint[] coordinates)
        => new(toleranceMetres: 0.0, coordinates, isPassthrough: true);
}

/// <summary>
/// A 2-D point in the map projection frame (EPSG:3857 metres). A value type
/// so the pyramid stores coordinates flat with no per-vertex allocation.
/// </summary>
internal readonly record struct CartesianPoint(double X, double Y);
