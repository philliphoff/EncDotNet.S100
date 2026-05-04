using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Geodesy;

/// <summary>
/// Marine-navigation geodesy helpers used by the Measure Mode tool.
/// </summary>
/// <remarks>
/// All inputs are WGS-84 latitude/longitude in degrees. Distances are
/// returned in nautical miles. Bearings are true (relative to true north),
/// in the closed-open range [0°, 360°).
///
/// <para>
/// V1 implements rhumb-line (loxodrome) calculations only — the standard
/// ECDIS convention for Distance/Bearing read-outs. A future
/// great-circle helper would live alongside these methods so call sites
/// can swap by selecting a different routine.
/// </para>
/// </remarks>
internal static class MarineGeodesy
{
    /// <summary>Mean Earth radius in nautical miles (6371 km / 1.852).</summary>
    private const double EarthRadiusNm = 3440.065;

    /// <summary>
    /// Maximum absolute latitude (degrees) used when evaluating rhumb-line
    /// formulas. Clamped to avoid the singularity at the poles where the
    /// Mercator projection (<c>ln tan(π/4 + φ/2)</c>) blows up.
    /// </summary>
    private const double LatitudeClampDegrees = 89.99;

    /// <summary>
    /// Computes the rhumb-line distance, in nautical miles, between two
    /// WGS-84 points along a constant true bearing (loxodrome).
    /// </summary>
    public static double RhumbDistanceNm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var phi1 = ClampLatRadians(lat1Deg);
        var phi2 = ClampLatRadians(lat2Deg);
        var dphi = phi2 - phi1;
        var dlam = NormalizeDeltaLonRadians(DegToRad(lon2Deg - lon1Deg));

        // Stretched (Mercator) latitude difference.
        var dpsi = Math.Log(Math.Tan(Math.PI / 4.0 + phi2 / 2.0) /
                            Math.Tan(Math.PI / 4.0 + phi1 / 2.0));

        // q = dphi/dpsi normally; degenerate to cos(phi1) when latitudes match.
        var q = Math.Abs(dpsi) > 1e-12 ? dphi / dpsi : Math.Cos(phi1);

        var distRadians = Math.Sqrt(dphi * dphi + q * q * dlam * dlam);
        return distRadians * EarthRadiusNm;
    }

    /// <summary>
    /// Computes the rhumb-line true bearing in degrees [0°, 360°) from the
    /// first point to the second.
    /// </summary>
    public static double RhumbBearingDegrees(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
    {
        var phi1 = ClampLatRadians(lat1Deg);
        var phi2 = ClampLatRadians(lat2Deg);
        var dlam = NormalizeDeltaLonRadians(DegToRad(lon2Deg - lon1Deg));

        var dpsi = Math.Log(Math.Tan(Math.PI / 4.0 + phi2 / 2.0) /
                            Math.Tan(Math.PI / 4.0 + phi1 / 2.0));

        var theta = Math.Atan2(dlam, dpsi);
        var deg = RadToDeg(theta);
        return ((deg % 360.0) + 360.0) % 360.0;
    }

    /// <summary>
    /// Splits a sequence of waypoints into one or more sub-paths so that no
    /// segment crosses the antimeridian (±180° longitude). Each output
    /// sub-path uses unwrapped longitudes that are continuous across its
    /// own segments; this lets a Mercator renderer draw the path as
    /// straight Mercator lines without the "wrap around the world"
    /// artifact you get when consecutive points span e.g. 179°E and
    /// 179°W.
    /// </summary>
    /// <remarks>
    /// Within a sub-path the second-and-later longitudes may exceed the
    /// canonical [-180, 180] range — this is intentional so the renderer
    /// can project the segment as a single straight line in Mercator
    /// space. Each new sub-path resets to the canonical range at its
    /// first point.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<(double Lat, double Lon)>> SplitAtAntimeridian(
        IEnumerable<(double Lat, double Lon)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<List<(double Lat, double Lon)>>();
        List<(double Lat, double Lon)>? current = null;
        double prevLon = 0.0;

        foreach (var pt in points)
        {
            if (current is null)
            {
                current = new List<(double Lat, double Lon)> { pt };
                prevLon = pt.Lon;
                result.Add(current);
                continue;
            }

            // If the longitude jump exceeds 180° in either direction, the
            // shorter rhumb crossed the antimeridian — start a new sub-path.
            var rawDelta = pt.Lon - prevLon;
            if (rawDelta > 180.0 || rawDelta < -180.0)
            {
                current = new List<(double Lat, double Lon)> { pt };
                result.Add(current);
                prevLon = pt.Lon;
            }
            else
            {
                var unwrapped = prevLon + rawDelta;
                current.Add((pt.Lat, unwrapped));
                prevLon = unwrapped;
            }
        }

        return result;
    }

    private static double DegToRad(double deg) => deg * (Math.PI / 180.0);
    private static double RadToDeg(double rad) => rad * (180.0 / Math.PI);

    private static double ClampLatRadians(double latDeg)
    {
        if (latDeg > LatitudeClampDegrees) latDeg = LatitudeClampDegrees;
        else if (latDeg < -LatitudeClampDegrees) latDeg = -LatitudeClampDegrees;
        return DegToRad(latDeg);
    }

    private static double NormalizeDeltaLonRadians(double dlamRadians)
    {
        // Wrap the longitude difference into (-π, π] so we always traverse
        // the short way around the globe.
        const double TwoPi = 2.0 * Math.PI;
        if (dlamRadians > Math.PI) dlamRadians -= TwoPi;
        else if (dlamRadians < -Math.PI) dlamRadians += TwoPi;
        return dlamRadians;
    }
}
