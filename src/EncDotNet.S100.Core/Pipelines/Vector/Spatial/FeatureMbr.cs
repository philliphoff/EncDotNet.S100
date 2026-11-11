namespace EncDotNet.S100.Pipelines.Vector.Spatial;

/// <summary>
/// Compact axis-aligned MBR used inside <see cref="StrRTree"/>. Value
/// type so tree nodes are cheap and cache-friendly. Coordinates carry
/// the same units and CRS as the enclosing feature (typically decimal
/// degrees, WGS-84).
/// </summary>
internal readonly struct FeatureMbr
{
    public double MinLat { get; }
    public double MinLon { get; }
    public double MaxLat { get; }
    public double MaxLon { get; }

    public FeatureMbr(double minLat, double minLon, double maxLat, double maxLon)
    {
        MinLat = minLat;
        MinLon = minLon;
        MaxLat = maxLat;
        MaxLon = maxLon;
    }

    public static FeatureMbr Union(in FeatureMbr a, in FeatureMbr b) => new(
        Math.Min(a.MinLat, b.MinLat),
        Math.Min(a.MinLon, b.MinLon),
        Math.Max(a.MaxLat, b.MaxLat),
        Math.Max(a.MaxLon, b.MaxLon));

    /// <summary>
    /// Closed-interval intersection: two MBRs touching on an edge count as
    /// intersecting. Preserves parity with the pre-existing
    /// <c>lat &gt;= south &amp;&amp; lat &lt;= north &amp;&amp; lon &gt;= west &amp;&amp; lon &lt;= east</c>
    /// scan in <c>S101VectorSource.IntersectsExtent</c>.
    /// </summary>
    public bool Intersects(BoundingBox extent) =>
        MaxLat >= extent.SouthLatitude
        && MinLat <= extent.NorthLatitude
        && MaxLon >= extent.WestLongitude
        && MinLon <= extent.EastLongitude;

    public double CenterLat => (MinLat + MaxLat) * 0.5;
    public double CenterLon => (MinLon + MaxLon) * 0.5;

    public BoundingBox ToBoundingBox() =>
        new(MinLat, MinLon, MaxLat, MaxLon);

    /// <summary>
    /// Computes the MBR of a <see cref="Feature"/> from its
    /// <see cref="Feature.Coordinates"/> and
    /// <see cref="Feature.InteriorRings"/>. Returns <see langword="null"/>
    /// when the feature has no coordinates.
    /// </summary>
    public static FeatureMbr? Compute(Feature feature)
    {
        var coords = feature.Coordinates;
        if (coords.Count == 0)
        {
            return null;
        }

        var first = coords[0];
        double minLat = first.Latitude, maxLat = first.Latitude;
        double minLon = first.Longitude, maxLon = first.Longitude;

        for (var i = 1; i < coords.Count; i++)
        {
            var (lat, lon) = coords[i];
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        // Interior rings can extend the surface's overall MBR only when
        // they poke outside the exterior, which is malformed; still fold
        // them in defensively so the MBR bounds every vertex the feature
        // exposes to downstream renderers/queries.
        var interior = feature.InteriorRings;
        for (var r = 0; r < interior.Count; r++)
        {
            var ring = interior[r];
            for (var i = 0; i < ring.Count; i++)
            {
                var (lat, lon) = ring[i];
                if (lat < minLat) minLat = lat;
                if (lat > maxLat) maxLat = lat;
                if (lon < minLon) minLon = lon;
                if (lon > maxLon) maxLon = lon;
            }
        }

        return new FeatureMbr(minLat, minLon, maxLat, maxLon);
    }
}
