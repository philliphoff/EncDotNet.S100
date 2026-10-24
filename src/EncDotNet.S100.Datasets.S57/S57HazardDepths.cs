using System.Globalization;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// Derives the S-101 <c>surroundingDepth</c> and <c>defaultClearanceDepth</c>
/// of an underwater hazard (Obstruction, UnderwaterAwashRock, Wreck) from the
/// S-57 depth areas it lies in.
/// </summary>
/// <remarks>
/// <para>
/// S-52 computed these at display time: DEPVAL02 takes the least DRVAL1 of the
/// DEPARE / DRGARE objects under the hazard, and OBSTRN07 / WRECKS05 fall back
/// on WATLEV / CATOBS defaults when VALSOU is missing. The S-101 portrayal
/// catalogue instead reads them as attributes of the feature, which S-57 does
/// not carry. Its OBSTRN07 and WRECKS05 rules raise an error when neither
/// <c>valueOfSounding</c> nor <c>defaultClearanceDepth</c> is present, which
/// portrays the hazard with the "?" default symbol, and UDWHAZ05 treats a
/// hazard without <c>surroundingDepth</c> as an isolated danger.
/// </para>
/// <para>
/// So the translator supplies them, following the S-52 logic:
/// <c>surroundingDepth</c> is the least DRVAL1 of the depth areas under the
/// hazard (a point inside, a curve or surface overlapping). When there is no
/// VALSOU, <c>defaultClearanceDepth</c> is that depth for an always-submerged
/// hazard whose sounding is within or deeper than the surrounding range
/// (WATLEV 3, EXPSOU 1 or 3); otherwise 0.01 for a foul area (CATOBS 6) or an
/// always-submerged hazard, 0 for one awash (WATLEV 5), and -15 for the rest
/// (covers and uncovers, dry, or unknown).
/// </para>
/// </remarks>
internal sealed class S57HazardDepths
{
    private readonly List<DepthArea> _areas;

    /// <summary>Initialises the index over the cell's depth areas.</summary>
    /// <param name="areas">
    /// Each depth area's DRVAL1 and boundary, as polylines of (Lat, Lon)
    /// positions. The polylines need not be chained into rings: containment is
    /// an even-odd crossing count over every boundary segment.
    /// </param>
    public S57HazardDepths(IEnumerable<(double Drval1, List<List<(double Lat, double Lon)>> Boundary)> areas)
    {
        _areas = areas
            .Where(a => a.Boundary.Count > 0)
            .Select(a => new DepthArea(a.Drval1, a.Boundary))
            .ToList();
    }

    /// <summary>
    /// The least DRVAL1 of the depth areas under a hazard, or
    /// <see langword="null"/> when none is.
    /// </summary>
    /// <param name="hazard">The hazard's geometry as (Lat, Lon) parts.</param>
    /// <param name="isPoint">Whether the hazard is a point (tested for containment only).</param>
    public double? SurroundingDepth(List<List<(double Lat, double Lon)>> hazard, bool isPoint)
    {
        var bounds = Bounds.Of(hazard);
        if (bounds is null) return null;

        double? least = null;
        foreach (var area in _areas)
        {
            if (least is { } l && area.Drval1 >= l) continue;
            if (!area.Bounds.Intersects(bounds.Value)) continue;
            if (isPoint ? area.Contains(hazard[0][0]) : area.Overlaps(hazard))
                least = area.Drval1;
        }
        return least;
    }

    /// <summary>
    /// The default clearance depth of a hazard without VALSOU (S-52 OBSTRN07 /
    /// WRECKS05 via DEPVAL02).
    /// </summary>
    /// <param name="surroundingDepth">The least depth of the depth areas under the hazard.</param>
    /// <param name="watlev">The S-57 WATLEV value, if any.</param>
    /// <param name="expsou">The S-57 EXPSOU value, if any.</param>
    /// <param name="catobs">The S-57 CATOBS value, if any.</param>
    public static double DefaultClearanceDepth(double? surroundingDepth, string? watlev, string? expsou, string? catobs)
    {
        if (surroundingDepth is { } depth && watlev == "3" && expsou is "1" or "3")
            return depth;
        if (catobs == "6" || watlev == "3") return 0.01;
        if (watlev == "5") return 0;
        return -15;
    }

    /// <summary>Formats a depth for an S-101 real attribute value.</summary>
    public static string Format(double depth) => depth.ToString("R", CultureInfo.InvariantCulture);

    private readonly record struct Bounds(double MinLat, double MinLon, double MaxLat, double MaxLon)
    {
        public bool Intersects(Bounds other)
            => MinLat <= other.MaxLat && other.MinLat <= MaxLat
            && MinLon <= other.MaxLon && other.MinLon <= MaxLon;

        public static Bounds? Of(List<List<(double Lat, double Lon)>> parts)
        {
            double minLat = double.MaxValue, minLon = double.MaxValue;
            double maxLat = double.MinValue, maxLon = double.MinValue;
            foreach (var part in parts)
            {
                foreach (var (lat, lon) in part)
                {
                    minLat = Math.Min(minLat, lat);
                    maxLat = Math.Max(maxLat, lat);
                    minLon = Math.Min(minLon, lon);
                    maxLon = Math.Max(maxLon, lon);
                }
            }
            return minLat > maxLat ? null : new Bounds(minLat, minLon, maxLat, maxLon);
        }
    }

    private sealed class DepthArea(double drval1, List<List<(double Lat, double Lon)>> boundary)
    {
        public double Drval1 { get; } = drval1;

        public Bounds Bounds { get; } = Bounds.Of(boundary)!.Value;

        public bool Contains((double Lat, double Lon) point)
        {
            bool inside = false;
            foreach (var part in boundary)
            {
                for (int i = 1; i < part.Count; i++)
                {
                    var (yi, xi) = part[i - 1];
                    var (yj, xj) = part[i];
                    if ((yi > point.Lat) != (yj > point.Lat)
                        && point.Lon < (xj - xi) * (point.Lat - yi) / (yj - yi) + xi)
                        inside = !inside;
                }
            }
            return inside;
        }

        // A curve or surface overlaps the area when one of its vertices lies
        // inside it or one of its segments crosses the area's boundary.
        public bool Overlaps(List<List<(double Lat, double Lon)>> hazard)
        {
            foreach (var part in hazard)
            {
                foreach (var p in part)
                    if (Contains(p)) return true;
            }
            foreach (var part in hazard)
            {
                for (int i = 1; i < part.Count; i++)
                {
                    foreach (var edge in boundary)
                    {
                        for (int j = 1; j < edge.Count; j++)
                        {
                            if (SegmentsCross(part[i - 1], part[i], edge[j - 1], edge[j]))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool SegmentsCross(
            (double Lat, double Lon) a, (double Lat, double Lon) b,
            (double Lat, double Lon) c, (double Lat, double Lon) d)
        {
            static double Cross((double Lat, double Lon) o, (double Lat, double Lon) p, (double Lat, double Lon) q)
                => (p.Lon - o.Lon) * (q.Lat - o.Lat) - (p.Lat - o.Lat) * (q.Lon - o.Lon);

            double d1 = Cross(c, d, a), d2 = Cross(c, d, b);
            double d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
    }
}
