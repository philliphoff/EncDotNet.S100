using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Geodesy;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>
/// How far winds of at least <see cref="ThresholdKnots"/> reach from the
/// storm's centre in each compass quadrant, at one time, and the area NHC
/// draws for it.
/// </summary>
/// <remarks>
/// A quadrant of 0 has no winds of that strength. NHC writes a radius whose
/// quadrants are all 0 when a storm has none (a depression's 34 kt row), and
/// the reader leaves those out, so <see cref="TropicalCycloneForecastPoint.Radii"/>
/// and <see cref="TropicalCycloneTrackPoint.Radii"/> only hold radii that
/// reach somewhere.
/// </remarks>
/// <param name="ThresholdKnots">The wind speed, in knots: 34, 50 or 64.</param>
/// <param name="NortheastNauticalMiles">The extent in the northeast quadrant, in nautical miles.</param>
/// <param name="SoutheastNauticalMiles">The extent in the southeast quadrant, in nautical miles.</param>
/// <param name="SouthwestNauticalMiles">The extent in the southwest quadrant, in nautical miles.</param>
/// <param name="NorthwestNauticalMiles">The extent in the northwest quadrant, in nautical miles.</param>
/// <param name="Surfaces">
/// NHC's polygon for the radius, in WGS-84 degrees; empty when the file had
/// none. It is NHC's construction, drawn as given (design note V2).
/// </param>
public sealed record TropicalCycloneWindRadii(
    int ThresholdKnots,
    double NortheastNauticalMiles,
    double SoutheastNauticalMiles,
    double SouthwestNauticalMiles,
    double NorthwestNauticalMiles,
    IReadOnlyList<SurfacePart> Surfaces)
{
    /// <summary>The largest extent of the four quadrants, in nautical miles.</summary>
    public double MaximumNauticalMiles =>
        Math.Max(Math.Max(NortheastNauticalMiles, SoutheastNauticalMiles), Math.Max(SouthwestNauticalMiles, NorthwestNauticalMiles));

    /// <summary>Whether no quadrant has winds of this strength.</summary>
    public bool IsEmpty => MaximumNauticalMiles <= 0;

    /// <summary>
    /// The area of this radius around <paramref name="centre"/>, built from
    /// its four quadrant extents: a quarter circle per quadrant, north to
    /// east, east to south, south to west and west to north, each at its own
    /// radius, on the WGS-84 ellipsoid. For drawing a radius that has no
    /// polygon of NHC's, such as a best-track point's.
    /// </summary>
    /// <param name="centre">The storm's centre.</param>
    /// <param name="pointsPerQuadrant">The number of points on each quarter circle, at least 2.</param>
    /// <returns>The area's surface; empty when no quadrant reaches anywhere.</returns>
    public IReadOnlyList<SurfacePart> QuadrantSurface(GeoPosition centre, int pointsPerQuadrant)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pointsPerQuadrant, 2);
        if (IsEmpty)
            return [];

        const double metresPerNauticalMile = 1852.0;
        double[] extents = [NortheastNauticalMiles, SoutheastNauticalMiles, SouthwestNauticalMiles, NorthwestNauticalMiles];
        var ring = new List<GeoPosition>(4 * pointsPerQuadrant + 1);
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            double metres = extents[quadrant] * metresPerNauticalMile;
            for (int i = 0; i < pointsPerQuadrant; i++)
            {
                double bearing = 90.0 * quadrant + 90.0 * i / (pointsPerQuadrant - 1);
                ring.Add(metres <= 0 ? centre : Wgs84Geodesy.GeodesicDestination(centre, bearing, metres));
            }
        }

        ring.Add(ring[0]);
        return [new SurfacePart(ring, [])];
    }
}
