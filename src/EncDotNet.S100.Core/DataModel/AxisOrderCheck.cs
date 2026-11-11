using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DataModel;

/// <summary>
/// The shared, dataset-level check for swapped latitude and longitude in
/// producer data (issue #650). Every coverage, station and GML reader runs it
/// once per dataset, after reading the positions as encoded, and applies the
/// outcome to the whole dataset, never per coordinate.
/// </summary>
/// <remarks>
/// <para>
/// The S-100 encodings put latitude first: Part 10b §6.2 for <c>EPSG:4326</c>
/// in GML, and the named <c>latitude</c> / <c>longitude</c> members and
/// attributes of Part 10c for HDF5. Real producer data swaps them often enough
/// (UKHO S-104 Solent stations, UK S-122, Danish S-128, US NWS / NIC S-411)
/// that readers must check rather than trust.
/// </para>
/// <para>
/// The check is evidence based and conservative. In order:
/// </para>
/// <list type="number">
/// <item><description><b>Physical bounds.</b> A latitude beyond ±90° can
/// only be a longitude. If any position has one and every longitude is within
/// ±90°, the dataset is <see cref="AxisOrderOutcome.Swapped"/>; if the
/// longitudes are out of range too, it is
/// <see cref="AxisOrderOutcome.Inconsistent"/>.</description></item>
/// <item><description><b>Declared extent.</b> When the dataset declares a
/// plausible extent, count the positions inside it as encoded and swapped
/// (padded by 5% of its span, at least 0.01°). At least 75% inside as encoded
/// is <see cref="AxisOrderOutcome.AsEncoded"/>; fewer than 25% as encoded
/// and more than 75% swapped is <see cref="AxisOrderOutcome.Swapped"/>;
/// anything else is <see cref="AxisOrderOutcome.Inconsistent"/>. These are
/// the thresholds the S-122 and S-128 readers used before they shared this
/// check.</description></item>
/// <item><description>A GML <c>gml:Envelope</c> is written in the same
/// ordinal order as the positions, so it can be the swapped one: the IEC
/// S-421 test routes have a longitude-first envelope around latitude-first
/// waypoints, the mirror image of the UK S-122 trial. An envelope that fits
/// only the swapped positions is therefore trusted only when the positions
/// cover at least half its span on both axes, as an envelope computed from the
/// data does; otherwise the outcome is
/// <see cref="AxisOrderOutcome.Inconsistent"/> with
/// <see cref="AxisOrderAssessment.ExtentMayBeSwapped"/> set. The HDF5
/// bounding-box attributes name their edges and need no such
/// test.</description></item>
/// <item><description>Otherwise <see cref="AxisOrderOutcome.Unverified"/>:
/// latitude first is assumed and the assessment says so.</description></item>
/// </list>
/// <para>
/// Only <see cref="AxisOrderOutcome.Swapped"/> changes positions; the other
/// outcomes keep them as encoded.
/// </para>
/// </remarks>
public static class AxisOrderCheck
{
    /// <summary>
    /// Share of positions that must fit the extent as encoded to accept it.
    /// </summary>
    private const double AcceptShare = 0.75;

    /// <summary>
    /// Share of positions below which the as-encoded reading is rejected.
    /// </summary>
    private const double RejectShare = 0.25;

    /// <summary>
    /// Assesses the axis order of <paramref name="positions"/>, read as
    /// encoded (first ordinate or <c>latitude</c> member as latitude).
    /// </summary>
    /// <param name="positions">Every position of the dataset, as encoded.</param>
    /// <param name="declaredExtent">
    /// The extent the dataset declares, or <c>null</c>. An implausible extent
    /// (latitudes beyond ±90°, or all zero) is ignored.
    /// </param>
    /// <param name="source">
    /// Where the positions come from, quoted in <see cref="AxisOrderAssessment.BuildMessage"/>.
    /// </param>
    /// <param name="normalizes">
    /// Whether the caller swaps the positions when the outcome is
    /// <see cref="AxisOrderOutcome.Swapped"/>; recorded as
    /// <see cref="AxisOrderAssessment.Normalized"/>. Pass <c>false</c> from a
    /// reader that only reports.
    /// </param>
    /// <param name="extentIsOrdinal">
    /// <c>true</c> when <paramref name="declaredExtent"/> was itself read
    /// from ordinals in an assumed order (a GML <c>gml:Envelope</c>), so it
    /// can be swapped just as the positions can; <c>false</c> (the default)
    /// when its edges are named (the HDF5 <c>westBoundLongitude</c> family).
    /// An ordinal extent that fits only the swapped positions is trusted only
    /// when it fits them tightly; see the remarks on <see cref="AxisOrderCheck"/>.
    /// </param>
    /// <returns>The assessment, or <c>null</c> when there are no positions.</returns>
    public static AxisOrderAssessment? Assess(
        IEnumerable<GeoPosition> positions,
        BoundingBox? declaredExtent,
        string? source = null,
        bool normalizes = true,
        bool extentIsOrdinal = false)
    {
        var assessment = AssessCore(positions, declaredExtent, source, extentIsOrdinal);
        return assessment is { Outcome: AxisOrderOutcome.Swapped } && normalizes
            ? assessment with { Normalized = true }
            : assessment;
    }

    private static AxisOrderAssessment? AssessCore(
        IEnumerable<GeoPosition> positions,
        BoundingBox? declaredExtent,
        string? source,
        bool extentIsOrdinal)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var extent = IsPlausible(declaredExtent) ? NormalizeWestEast(declaredExtent!) : null;
        var padded = extent is null ? null : Pad(extent);

        int total = 0, outOfBounds = 0, fitAsEncoded = 0, fitSwapped = 0;
        bool longitudeBeyond90 = false;
        double minLat = double.MaxValue, maxLat = double.MinValue, minLon = double.MaxValue, maxLon = double.MinValue;
        foreach (var p in positions)
        {
            if (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude))
                continue;

            total++;
            if (Math.Abs(p.Latitude) > 90.0)
                outOfBounds++;
            if (Math.Abs(p.Longitude) > 90.0)
                longitudeBeyond90 = true;
            minLat = Math.Min(minLat, p.Latitude);
            maxLat = Math.Max(maxLat, p.Latitude);
            minLon = Math.Min(minLon, p.Longitude);
            maxLon = Math.Max(maxLon, p.Longitude);

            if (padded is not null)
            {
                if (Contains(padded, p.Latitude, p.Longitude)) fitAsEncoded++;
                if (Contains(padded, p.Longitude, p.Latitude)) fitSwapped++;
            }
        }

        if (total == 0)
            return null;

        var assessment = new AxisOrderAssessment
        {
            Outcome = AxisOrderOutcome.Unverified,
            Evidence = AxisOrderEvidence.None,
            PositionCount = total,
            FitAsEncoded = fitAsEncoded,
            FitSwapped = fitSwapped,
            OutOfPhysicalBounds = outOfBounds,
            DeclaredExtent = extent,
            Source = source,
        };

        if (outOfBounds > 0)
        {
            return assessment with
            {
                Outcome = longitudeBeyond90 ? AxisOrderOutcome.Inconsistent : AxisOrderOutcome.Swapped,
                Evidence = AxisOrderEvidence.PhysicalBounds,
            };
        }

        if (extent is null)
            return assessment;

        if (fitAsEncoded >= total * AcceptShare)
            return assessment with { Outcome = AxisOrderOutcome.AsEncoded, Evidence = AxisOrderEvidence.DeclaredExtent };

        if (fitAsEncoded < total * RejectShare && fitSwapped > total * AcceptShare)
        {
            // Read swapped, the positions span latitudes minLon..maxLon and
            // longitudes minLat..maxLat. An ordinal envelope that disagrees is
            // trusted only when it is tight around them, as an envelope
            // computed from the data is; a loose, hand-written region may be
            // the swapped one (the IEC S-421 test routes).
            if (extentIsOrdinal && !IsTight(extent, minLon, maxLon, minLat, maxLat))
            {
                return assessment with
                {
                    Outcome = AxisOrderOutcome.Inconsistent,
                    Evidence = AxisOrderEvidence.DeclaredExtent,
                    ExtentMayBeSwapped = true,
                };
            }

            return assessment with { Outcome = AxisOrderOutcome.Swapped, Evidence = AxisOrderEvidence.DeclaredExtent };
        }

        return assessment with { Outcome = AxisOrderOutcome.Inconsistent, Evidence = AxisOrderEvidence.DeclaredExtent };
    }

    /// <summary>
    /// Returns <paramref name="position"/> with latitude and longitude swapped.
    /// </summary>
    public static GeoPosition Swap(GeoPosition position) => new(position.Longitude, position.Latitude);

    /// <summary>
    /// Returns <paramref name="positions"/> swapped when
    /// <paramref name="assessment"/> is <see cref="AxisOrderOutcome.Swapped"/>,
    /// and unchanged otherwise.
    /// </summary>
    public static IReadOnlyList<GeoPosition> Apply(AxisOrderAssessment? assessment, IReadOnlyList<GeoPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (assessment?.Outcome != AxisOrderOutcome.Swapped || positions.Count == 0)
            return positions;

        var swapped = new GeoPosition[positions.Count];
        for (int i = 0; i < positions.Count; i++)
            swapped[i] = Swap(positions[i]);
        return swapped;
    }

    /// <summary>
    /// Returns the four corners of a regular grid's footprint, as encoded,
    /// for <see cref="Assess"/>: the origin and the far corner along each
    /// axis (S-100 Part 10c §10.2.1.2 grid georeferencing).
    /// </summary>
    public static IEnumerable<GeoPosition> GridCorners(
        double originLatitude,
        double originLongitude,
        double spacingLatitudinal,
        double spacingLongitudinal,
        int numPointsLatitudinal,
        int numPointsLongitudinal)
    {
        double farLatitude = originLatitude + spacingLatitudinal * Math.Max(0, numPointsLatitudinal - 1);
        double farLongitude = originLongitude + spacingLongitudinal * Math.Max(0, numPointsLongitudinal - 1);
        yield return new GeoPosition(originLatitude, originLongitude);
        yield return new GeoPosition(originLatitude, farLongitude);
        yield return new GeoPosition(farLatitude, originLongitude);
        yield return new GeoPosition(farLatitude, farLongitude);
    }

    /// <summary>
    /// A west edge east of the east edge means an extent across the
    /// antimeridian; one that would then span more than half the world is
    /// more likely written with its edges reversed (seen in IC-ENC Peru S-111,
    /// west −68°, east −98°), and is read with them swapped back.
    /// </summary>
    private static BoundingBox NormalizeWestEast(BoundingBox extent) =>
        extent.WestLongitude > extent.EastLongitude
            && extent.EastLongitude + 360.0 - extent.WestLongitude > 180.0
            ? new BoundingBox(extent.SouthLatitude, extent.EastLongitude, extent.NorthLatitude, extent.WestLongitude)
            : extent;

    private static bool IsPlausible(BoundingBox? extent)
    {
        if (extent is null)
            return false;

        double s = extent.SouthLatitude, n = extent.NorthLatitude, w = extent.WestLongitude, e = extent.EastLongitude;
        if (!double.IsFinite(s) || !double.IsFinite(n) || !double.IsFinite(w) || !double.IsFinite(e))
            return false;
        if (Math.Abs(s) > 90.0 || Math.Abs(n) > 90.0 || s > n)
            return false;
        if (w < -180.0 || w > 360.0 || e < -180.0 || e > 360.0)
            return false;

        // A zero-filled bounding box is a placeholder, not a declaration.
        return !(s == 0 && n == 0 && w == 0 && e == 0);
    }

    /// <summary>
    /// Share of the declared extent's span, on each axis, that the positions
    /// must cover for an ordinal extent to count as computed from them.
    /// </summary>
    private const double TightShare = 0.5;

    private static bool IsTight(BoundingBox extent, double south, double north, double west, double east)
    {
        double latSpan = extent.NorthLatitude - extent.SouthLatitude;
        double lonSpan = extent.WestLongitude <= extent.EastLongitude
            ? extent.EastLongitude - extent.WestLongitude
            : extent.EastLongitude + 360.0 - extent.WestLongitude;

        // A near-degenerate extent (a single point or line) is as tight as it gets.
        bool latTight = latSpan < 1e-6 || (north - south) >= latSpan * TightShare;
        bool lonTight = lonSpan < 1e-6 || (east - west) >= lonSpan * TightShare;
        return latTight && lonTight;
    }

    private static BoundingBox Pad(BoundingBox extent)
    {
        double lonSpan = extent.WestLongitude <= extent.EastLongitude
            ? extent.EastLongitude - extent.WestLongitude
            : extent.EastLongitude + 360.0 - extent.WestLongitude;
        double latPad = Math.Max(0.01, (extent.NorthLatitude - extent.SouthLatitude) * 0.05);
        double lonPad = Math.Max(0.01, lonSpan * 0.05);
        return new BoundingBox(
            extent.SouthLatitude - latPad,
            extent.WestLongitude - lonPad,
            extent.NorthLatitude + latPad,
            extent.EastLongitude + lonPad);
    }

    private static bool Contains(BoundingBox extent, double latitude, double longitude)
    {
        if (latitude < extent.SouthLatitude || latitude > extent.NorthLatitude)
            return false;

        // The extent may be in −180..180 or 0..360, and may cross the
        // antimeridian (west > east); test the longitude's world copies.
        for (int k = -1; k <= 1; k++)
        {
            double lon = longitude + k * 360.0;
            bool inside = extent.WestLongitude <= extent.EastLongitude
                ? lon >= extent.WestLongitude && lon <= extent.EastLongitude
                : lon >= extent.WestLongitude || lon <= extent.EastLongitude;
            if (inside)
                return true;
        }

        return false;
    }
}
