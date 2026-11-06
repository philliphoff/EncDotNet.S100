using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The horizontal EPSG:3857 world copies chart content is drawn at (issue
/// #773): the dataset's own frame (<c>0</c>) and the copies one
/// <see cref="WebMercator.Circumference"/> east and west, matching the offline
/// basemap's world copies.
/// </summary>
/// <remarks>
/// A dataset keeps the longitude frame it was encoded in. The NIC Arctic S-411
/// runs 0…360° and the NWS Alaska S-411 runs ~175°E → ~225°E, so drawing either
/// once leaves part of it in the adjacent world copy: a view of Fram Strait
/// showed the ice cut at 0°. Drawing every copy that meets the view shows the
/// data wherever the viewer can pan, as the basemap does. The data itself,
/// its extent and its tiles stay in the raw frame; a copy is only a draw-time
/// X offset.
/// </remarks>
internal static class WorldCopies
{
    /// <summary>The EPSG:3857 world width in metres.</summary>
    public const double Circumference = WebMercator.Circumference;

    // One cached array per subset of {-C, 0, +C} (bit 0 = west, bit 1 = own
    // frame, bit 2 = east), so the per-frame query never allocates.
    private static readonly double[][] Subsets = BuildSubsets();

    /// <summary>Every world-copy X offset, west to east.</summary>
    public static IReadOnlyList<double> OffsetsX => Subsets[0b111];

    /// <summary>Only the dataset's own frame (no copies).</summary>
    public static IReadOnlyList<double> OwnFrame => Subsets[0b010];

    /// <summary>No copy (the content is out of view).</summary>
    public static IReadOnlyList<double> None => Subsets[0];

    /// <summary>
    /// The X offsets of the copies of the content span
    /// [<paramref name="minX"/>, <paramref name="maxX"/>] that overlap the view
    /// span [<paramref name="viewMinX"/>, <paramref name="viewMaxX"/>], west to
    /// east. The content span is grown by <paramref name="marginWorld"/> on both
    /// sides so symbols on content just off-view still draw.
    /// </summary>
    public static IReadOnlyList<double> Visible(
        double minX, double maxX, double viewMinX, double viewMaxX, double marginWorld)
    {
        var mask = 0;
        var offsets = Subsets[0b111];
        for (var i = 0; i < offsets.Length; i++)
        {
            var offset = offsets[i];
            if (maxX + offset + marginWorld >= viewMinX && minX + offset - marginWorld <= viewMaxX)
            {
                mask |= 1 << i;
            }
        }

        return Subsets[mask];
    }

    /// <summary>
    /// The offset in <paramref name="offsets"/> whose copy of the content span
    /// lies closest to <paramref name="centerX"/>; <c>0</c> when
    /// <paramref name="offsets"/> is empty. Used where one copy must be chosen,
    /// such as the centre that orders the tile queue.
    /// </summary>
    public static double Nearest(IReadOnlyList<double> offsets, double minX, double maxX, double centerX)
    {
        var best = 0.0;
        var bestDistance = double.PositiveInfinity;
        foreach (var offset in offsets)
        {
            var lo = minX + offset;
            var hi = maxX + offset;
            var distance = centerX < lo ? lo - centerX : centerX > hi ? centerX - hi : 0.0;
            if (distance < bestDistance)
            {
                best = offset;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// The longitudes, in degrees, to hit-test a dataset at for a pick at
    /// <paramref name="longitude"/>: the pick itself first, then the points one
    /// world east and west, which the drawn world copies place under the
    /// pointer. The shifted candidates are kept only when they fall within the
    /// dataset's raw longitude span (<paramref name="westLongitude"/> to
    /// <paramref name="eastLongitude"/>, padded by a degree), so a pick tests a
    /// dataset at most once more than before and only where a copy can be.
    /// An unknown span tests the pick alone.
    /// </summary>
    public static IEnumerable<double> CandidateLongitudes(
        double longitude, double? westLongitude, double? eastLongitude)
    {
        yield return longitude;
        if (westLongitude is not { } west || eastLongitude is not { } east)
        {
            yield break;
        }

        const double pad = 1.0;
        if (longitude + 360.0 <= east + pad)
        {
            yield return longitude + 360.0;
        }

        if (longitude - 360.0 >= west - pad)
        {
            yield return longitude - 360.0;
        }
    }

    private static double[][] BuildSubsets()
    {
        double[] all = [-Circumference, 0.0, Circumference];
        var subsets = new double[8][];
        for (var mask = 0; mask < subsets.Length; mask++)
        {
            var list = new List<double>(3);
            for (var i = 0; i < all.Length; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    list.Add(all[i]);
                }
            }

            subsets[mask] = [.. list];
        }

        return subsets;
    }
}
