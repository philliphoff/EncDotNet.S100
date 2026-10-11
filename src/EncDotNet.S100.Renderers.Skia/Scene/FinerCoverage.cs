using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// The data coverage of a finer chart cell that hides an overlapping coarser
/// cell where both draw: the coarser cell's layers are clipped to exclude it,
/// so the two don't double-draw (cross-cell scale-band overlap suppression, as
/// the viewer applies it).
/// </summary>
/// <remarks>
/// The coverage only hides while the finer cell is itself drawing, so a viewport
/// zoomed out past <see cref="MaximumScaleDenominator"/> shows the coarser cell
/// in full instead of a blank hole. Rings are filled with the even-odd rule, so
/// a no-coverage hole in the finer cell lets the coarser cell show through.
/// </remarks>
public sealed class FinerCoverage
{
    /// <summary>Creates a finer coverage.</summary>
    /// <param name="worldRings">
    /// The coverage rings in EPSG:3857 metres, exterior rings and holes alike.
    /// </param>
    /// <param name="maximumScaleDenominator">
    /// The coarsest scale denominator at which the finer cell still draws, such
    /// as its minimum display scale, or <see langword="null"/> when it draws at
    /// every scale.
    /// </param>
    public FinerCoverage(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> worldRings,
        double? maximumScaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(worldRings);
        WorldRings = worldRings;
        MaximumScaleDenominator = maximumScaleDenominator;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var ring in worldRings)
        {
            foreach (var (x, y) in ring)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        Bounds = (minX, minY, maxX, maxY);
    }

    /// <summary>The coverage rings in EPSG:3857 metres.</summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> WorldRings { get; }

    /// <summary>
    /// The coarsest scale denominator at which the finer cell draws, or
    /// <see langword="null"/> for every scale.
    /// </summary>
    public double? MaximumScaleDenominator { get; }

    /// <summary>The EPSG:3857 bounds of <see cref="WorldRings"/>.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds { get; }

    /// <summary>Whether the coverage has at least one ring of three or more points.</summary>
    public bool IsEmpty => !WorldRings.Any(ring => ring.Count >= 3);

    /// <summary>
    /// Whether the finer cell draws at <paramref name="scaleDenominator"/>, and
    /// so hides the coarser cell there.
    /// </summary>
    /// <param name="scaleDenominator">The viewport's scale denominator.</param>
    /// <param name="honorScaleVisibility">
    /// Whether scale visibility is applied at all; without it the finer cell
    /// draws at every scale.
    /// </param>
    public bool IsActiveAt(double scaleDenominator, bool honorScaleVisibility) =>
        !honorScaleVisibility
        || MaximumScaleDenominator is not double limit
        || scaleDenominator <= limit;

    /// <summary>
    /// Removes the active coverages in <paramref name="coverages"/> from the
    /// canvas's clip, projected through <paramref name="transform"/>. Coverages
    /// whose bounds miss <paramref name="screen"/> are skipped.
    /// </summary>
    internal static void ClipOut(
        SKCanvas canvas,
        IReadOnlyList<FinerCoverage> coverages,
        WorldToScreen transform,
        SKRect screen,
        double scaleDenominator,
        bool honorScaleVisibility)
    {
        foreach (var coverage in coverages)
        {
            if (!coverage.IsActiveAt(scaleDenominator, honorScaleVisibility) || coverage.IsEmpty)
                continue;

            var (minX, minY, maxX, maxY) = coverage.Bounds;
            var (left, top) = transform.Project((minX, maxY));
            var (right, bottom) = transform.Project((maxX, minY));
            if (!screen.IntersectsWith(new SKRect(
                    Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom))))
            {
                continue;
            }

            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var ring in coverage.WorldRings)
            {
                if (ring.Count < 3)
                    continue;

                var (x0, y0) = transform.Project(ring[0]);
                path.MoveTo(x0, y0);
                for (int i = 1; i < ring.Count; i++)
                {
                    var (x, y) = transform.Project(ring[i]);
                    path.LineTo(x, y);
                }

                path.Close();
            }

            canvas.ClipPath(path, SKClipOperation.Difference, antialias: true);
        }
    }
}
