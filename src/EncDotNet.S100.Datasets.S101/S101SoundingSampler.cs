using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// A single charted sounding depth sampled at a specific position, together
/// with its planar distance from the query point.
/// </summary>
/// <param name="Position">The WGS-84 position of the sounding point.</param>
/// <param name="DepthMeters">
/// The sounding value in metres (positive down), i.e. the multipoint Z
/// ordinate divided by the dataset's Z multiplication factor (SOMF).
/// </param>
/// <param name="DistanceMeters">
/// The equirectangular distance in metres from the query point to
/// <paramref name="Position"/>.
/// </param>
public readonly record struct S101SoundingSample(
    GeoPosition Position,
    double DepthMeters,
    double DistanceMeters);

/// <summary>
/// Samples the nearest charted sounding (S-101 <c>Sounding</c> feature) depth
/// to an arbitrary position.
/// </summary>
/// <remarks>
/// <para>
/// S-101 encodes soundings as multipoint spatial records (RCNM = 115) carrying
/// many 3-D coordinate triples, one per sounding, rather than as one feature
/// per depth. Ranking whole features by centroid therefore cannot surface the
/// nearest individual sounding — the search has to descend to the per-point Z
/// ordinates. This sampler performs that per-point nearest search across every
/// <c>Sounding</c> feature in the document (S-101 Annex A; S-100 Part 10a
/// §10a-6 spatial records).
/// </para>
/// <para>
/// Distances use the same fast equirectangular approximation
/// (<see cref="MetersPerDegreeLatitude"/> scaled by the cosine of the mean
/// latitude) used elsewhere in the codebase; over the span of a single ENC
/// cell the error is a fraction of a percent, which never changes which
/// sounding is nearest.
/// </para>
/// </remarks>
public static class S101SoundingSampler
{
    /// <summary>
    /// The S-101 feature catalogue code (Annex A) of the Sounding feature, as
    /// resolved through <see cref="S101Document.FeatureTypeCatalogue"/>.
    /// </summary>
    public const string SoundingFeatureType = "Sounding";

    /// <summary>Metres per degree of latitude (WGS-84 mean).</summary>
    public const double MetersPerDegreeLatitude = 111_320.0;

    /// <summary>RCNM value identifying a multi-point spatial record (S-100 Part 10a).</summary>
    private const byte RcnmMultiPoint = 115;

    /// <summary>
    /// Returns the nearest charted sounding to the given position, or
    /// <c>null</c> when the document carries no <c>Sounding</c> features with
    /// multipoint geometry.
    /// </summary>
    /// <param name="document">The parsed S-101 dataset to search.</param>
    /// <param name="latitude">Query latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Query longitude in decimal degrees (WGS-84).</param>
    /// <returns>
    /// The nearest <see cref="S101SoundingSample"/>, or <c>null</c> when no
    /// sounding points are present.
    /// </returns>
    public static S101SoundingSample? SampleNearest(S101Document document, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Coordinate multiplication factors (DSSI, S-100 Part 10a §10a-6.1.2.2):
        // typically COMF = 1e7 for lat/lon and SOMF = 100 for depth. The
        // defensive fallbacks mirror the Lua data provider and only fire for a
        // missing/partial DSSI (they never affect well-formed data).
        double cmfx = document.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = document.StructureInfo.CoordinateMultiplicationFactorY;
        double cmfz = document.StructureInfo.CoordinateMultiplicationFactorZ;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;
        if (cmfz == 0) cmfz = 10;

        double bestDistance = double.PositiveInfinity;
        S101SoundingSample? best = null;

        foreach (var feature in document.Features)
        {
            if (!IsSounding(document, feature))
            {
                continue;
            }

            foreach (var association in feature.SpatialAssociations)
            {
                if (association.RecordName != RcnmMultiPoint)
                {
                    continue;
                }

                if (!document.MultiPoints.TryGetValue(association.RecordId, out var multiPoint))
                {
                    continue;
                }

                foreach (var (y, x, z) in multiPoint.Points)
                {
                    double pointLat = y / cmfy;
                    double pointLon = x / cmfx;
                    double distance = Meters(latitude, longitude, pointLat, pointLon);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new S101SoundingSample(
                            new GeoPosition(pointLat, pointLon),
                            z / cmfz,
                            distance);
                    }
                }
            }
        }

        return best;
    }

    private static bool IsSounding(S101Document document, S101FeatureRecord feature) =>
        document.FeatureTypeCatalogue.TryGetValue(feature.FeatureTypeCode, out var name)
        && string.Equals(name, SoundingFeatureType, StringComparison.OrdinalIgnoreCase);

    private static double Meters(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat1 - lat2) * MetersPerDegreeLatitude;
        double dLon = (lon1 - lon2) * MetersPerDegreeLatitude
            * Math.Cos((lat1 + lat2) * 0.5 * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }
}
