using System.Runtime.CompilerServices;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// SCAMIN "scale classes" for base-plane tiles (issue #774). The live map shows
/// a band's tiles at display scales up to √2 finer or coarser than the band's
/// own (<see cref="TileGrid.BandForResolution"/> is log-nearest). Every op's own
/// <see cref="PaintOp.ScaleMinimum"/>/<see cref="PaintOp.ScaleMaximum"/> must
/// still be judged at the live scale, so a tile is keyed by the interval
/// between consecutive op thresholds its live scale falls in, and rasterised
/// at a denominator inside that interval.
/// </summary>
/// <remarks>
/// <para>The thresholds are the scene's distinct op scale limits. Only those
/// strictly inside a tile's band display window split it, so a tile whose band
/// window holds no threshold has the single class 0 and is rasterised at the
/// band's own denominator, as before. Class <c>c</c> holds the live
/// denominators with exactly <c>c</c> in-window thresholds below them.</para>
/// <para>The live denominator of a tile is its band denominator times
/// <c>resolution / ResolutionForBand(band)</c>. That ratio is the same for
/// every tile of a frame, and is kept for cross-band tiles warmed ahead of a
/// zoom.</para>
/// </remarks>
internal static class TileScaleClass
{
    private static readonly double Sqrt2 = Math.Sqrt(2.0);

    private static readonly ConditionalWeakTable<VectorScene, double[]> SceneThresholds = new();

    /// <summary>
    /// The distinct, positive op scale limits of <paramref name="scene"/>,
    /// ascending. Computed once per scene.
    /// </summary>
    public static double[] Thresholds(VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return SceneThresholds.GetValue(scene, static s => Collect(s.Ops));
    }

    /// <summary>The distinct, positive scale limits of <paramref name="ops"/>, ascending.</summary>
    internal static double[] Collect(IEnumerable<PaintOp> ops)
    {
        var values = new SortedSet<double>();
        foreach (var op in ops)
        {
            if (op.ScaleMinimum is double min && min > 0 && double.IsFinite(min))
            {
                values.Add(min);
            }

            if (op.ScaleMaximum is double max && max > 0 && double.IsFinite(max))
            {
                values.Add(max);
            }
        }

        return [.. values];
    }

    /// <summary>
    /// The scale class of a tile whose band denominator is
    /// <paramref name="bandDenominator"/>, shown at <paramref name="ratio"/>
    /// (<c>resolution / ResolutionForBand(band)</c>).
    /// </summary>
    public static int For(double[] thresholds, int band, double bandDenominator, double ratio)
    {
        var (start, end) = InWindow(thresholds, band, bandDenominator);
        if (start == end)
        {
            return 0;
        }

        var live = bandDenominator * ratio;
        return LowerBound(thresholds, start, end, live) - start;
    }

    /// <summary>The number of scale classes of a tile (at least one).</summary>
    public static int Count(double[] thresholds, int band, double bandDenominator)
    {
        var (start, end) = InWindow(thresholds, band, bandDenominator);
        return end - start + 1;
    }

    /// <summary>
    /// The denominator a tile of <paramref name="scaleClass"/> is rasterised
    /// at: the band's own denominator when it lies inside the class, otherwise
    /// a denominator well inside the class, so every op's visibility is the
    /// one it has at any live scale of the class.
    /// </summary>
    public static double Denominator(double[] thresholds, int band, double bandDenominator, int scaleClass)
    {
        var (start, end) = InWindow(thresholds, band, bandDenominator);
        if (start == end)
        {
            return bandDenominator;
        }

        var c = Math.Clamp(scaleClass, 0, end - start);
        var (windowLow, windowHigh) = Window(band, bandDenominator);
        var low = c == 0 ? windowLow : thresholds[start + c - 1];
        var high = c == end - start ? windowHigh : thresholds[start + c];
        if (bandDenominator > low && bandDenominator < high)
        {
            return bandDenominator;
        }

        if (low <= 0)
        {
            return high / 2.0;
        }

        if (double.IsPositiveInfinity(high))
        {
            return low * 2.0;
        }

        return Math.Sqrt(low * high);
    }

    /// <summary>
    /// The live denominators a band's tiles are shown at, exclusive. A band at
    /// either end of the grid is also shown past its √2 window, so its window
    /// is open on that side.
    /// </summary>
    internal static (double Low, double High) Window(int band, double bandDenominator) => (
        band >= TileGrid.MaxBand ? 0.0 : bandDenominator / Sqrt2,
        band <= TileGrid.MinBand ? double.PositiveInfinity : bandDenominator * Sqrt2);

    private static (int Start, int End) InWindow(double[] thresholds, int band, double bandDenominator)
    {
        if (thresholds.Length == 0)
        {
            return (0, 0);
        }

        var (low, high) = Window(band, bandDenominator);
        var start = UpperBound(thresholds, 0, thresholds.Length, low);
        var end = LowerBound(thresholds, start, thresholds.Length, high);
        return (start, end);
    }

    /// <summary>The first index in [start, end) whose value is not below <paramref name="value"/>.</summary>
    private static int LowerBound(double[] values, int start, int end, double value)
    {
        while (start < end)
        {
            var mid = start + ((end - start) >> 1);
            if (values[mid] < value)
            {
                start = mid + 1;
            }
            else
            {
                end = mid;
            }
        }

        return start;
    }

    /// <summary>The first index in [start, end) whose value is above <paramref name="value"/>.</summary>
    private static int UpperBound(double[] values, int start, int end, double value)
    {
        while (start < end)
        {
            var mid = start + ((end - start) >> 1);
            if (values[mid] <= value)
            {
                start = mid + 1;
            }
            else
            {
                end = mid;
            }
        }

        return start;
    }
}
