namespace EncDotNet.S100.Tides;

/// <summary>
/// The mean astronomical elements and the lunar node terms of Schureman,
/// <i>Manual of Harmonic Analysis and Prediction of Tides</i> (US Coast and
/// Geodetic Survey Special Publication 98, 1958 revision).
/// </summary>
internal static class Astronomy
{
    /// <summary>Schureman's epoch: Greenwich mean noon, 1899 December 31 (1900 January 0.5).</summary>
    private static readonly DateTime Epoch = new(1899, 12, 31, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Hours in a Julian century.</summary>
    private const double HoursPerCentury = 36525.0 * 24.0;

    /// <summary>Obliquity of the ecliptic, ω (SP 98 §§ 6, 137: 23° 27′ 8.26″ at 1900).</summary>
    private const double Obliquity = 23.452294;

    /// <summary>Inclination of the moon's orbit to the ecliptic, i (SP 98 § 6: 5° 08′ 43.3546″).</summary>
    private const double LunarInclination = 5.145376;

    /// <summary>
    /// The rates of the elements in degrees per mean solar hour, from the
    /// linear terms of SP 98 Table 1: T, s, h, p, N, p₁. The constituent
    /// speeds are combinations of these.
    /// </summary>
    public static readonly double[] Rates =
    [
        15.0,
        481267.8831 / HoursPerCentury,
        36000.768925 / HoursPerCentury,
        4069.034033 / HoursPerCentury,
        -1934.142008 / HoursPerCentury,
        1.719175 / HoursPerCentury,
    ];

    /// <summary>
    /// The mean elements at a time, in degrees (SP 98 Table 1): T, the hour
    /// angle of the mean sun; s, h and p, the mean longitudes of the moon,
    /// the sun and the lunar perigee; N, the longitude of the moon's
    /// ascending node; p₁, the longitude of the solar perigee.
    /// </summary>
    /// <param name="utc">The time (UTC).</param>
    /// <param name="elements">Receives T, s, h, p, N, p₁ in that order.</param>
    public static void Elements(DateTime utc, Span<double> elements)
    {
        var hours = (utc - Epoch).Ticks / (double)TimeSpan.TicksPerHour;
        var c = hours / HoursPerCentury;
        var c2 = c * c;
        var c3 = c2 * c;

        // T is 0 at Greenwich mean noon and advances 15° an hour, so it's
        // 180° at midnight as SP 98 § 98 has it (T = 180° + t).
        elements[0] = Angles.Normalize(15.0 * (hours % 24.0));
        elements[1] = Angles.Normalize(270.434164 + (481267.8831 * c) - (0.001133 * c2) + (0.0000019 * c3));
        elements[2] = Angles.Normalize(279.696678 + (36000.768925 * c) + (0.0003025 * c2));
        elements[3] = Angles.Normalize(334.329556 + (4069.034033 * c) - (0.010325 * c2) - (0.0000125 * c3));
        elements[4] = Angles.Normalize(259.183275 - (1934.142008 * c) + (0.002078 * c2) + (0.0000022 * c3));
        elements[5] = Angles.Normalize(281.220833 + (1.719175 * c) + (0.000453 * c2) + (0.000003 * c3));
    }

    /// <summary>
    /// The terms that depend on the longitude of the moon's node, N, and on
    /// the lunar perigee (SP 98 §§ 136–143, 205–235 and Tables 6–14).
    /// </summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The nodal terms.</returns>
    public static NodalTerms Nodal(DateTime utc)
    {
        Span<double> elements = stackalloc double[6];
        Elements(utc, elements);
        var p = elements[3] * Angles.DegreesToRadians;
        var n = elements[4] * Angles.DegreesToRadians;

        var omega = Obliquity * Angles.DegreesToRadians;
        var inclination = LunarInclination * Angles.DegreesToRadians;

        // The moon's orbit, the ecliptic and the equator form a spherical
        // triangle (SP 98 § 136, Figure 3). Rather than the half-angle
        // formulas, solve it with vectors in equatorial coordinates (x
        // towards the vernal equinox): the orbit's pole, its ascending node
        // on the equator (the lunar intersection) and its node on the
        // ecliptic.
        var sinI0 = Math.Sin(inclination);
        var cosI0 = Math.Cos(inclination);
        var sinW = Math.Sin(omega);
        var cosW = Math.Cos(omega);
        var sinN = Math.Sin(n);
        var cosN = Math.Cos(n);

        var poleX = sinI0 * sinN;
        var poleY = (-sinI0 * cosN * cosW) - (cosI0 * sinW);
        var poleZ = (-sinI0 * cosN * sinW) + (cosI0 * cosW);

        // I: the inclination of the moon's orbit to the equator (§ 137).
        var bigI = Math.Acos(poleZ);

        // ν: the right ascension of the lunar intersection (§ 138).
        var nu = Math.Atan2(poleX, -poleY);

        // ξ: the longitude in the moon's orbit of the lunar intersection
        // (§ 139): N less the arc of the orbit from the intersection to the
        // node.
        var horizontal = Math.Sqrt((poleX * poleX) + (poleY * poleY));
        var intersectionX = -poleY / horizontal;
        var intersectionY = poleX / horizontal;
        var nodeX = cosN;
        var nodeY = sinN * cosW;
        var nodeZ = sinN * sinW;
        var crossX = intersectionY * nodeZ;
        var crossY = -intersectionX * nodeZ;
        var crossZ = (intersectionX * nodeY) - (intersectionY * nodeX);
        var arc = Math.Atan2(
            (crossX * poleX) + (crossY * poleY) + (crossZ * poleZ),
            (intersectionX * nodeX) + (intersectionY * nodeY));
        var xi = n - arc;

        var sinI = Math.Sin(bigI);
        var cosI = Math.Cos(bigI);
        var sin2I = Math.Sin(2 * bigI);
        var halfCos = Math.Cos(bigI / 2);
        var halfTan = Math.Tan(bigI / 2);

        // ν′ and 2ν″, the K₁ and K₂ terms (§§ 224, 232).
        var nuPrime = Math.Atan2(sin2I * Math.Sin(nu), (sin2I * Math.Cos(nu)) + 0.3347);
        var twoNuSecond = Math.Atan2(sinI * sinI * Math.Sin(2 * nu), (sinI * sinI * Math.Cos(2 * nu)) + 0.0727);

        // P, the longitude of the lunar perigee from the lunar intersection
        // (§ 206), and the L₂ terms R and 1/Rₐ (§§ 213–215).
        var bigP = p - xi;
        var cos2P = Math.Cos(2 * bigP);
        var r = Math.Atan2(Math.Sin(2 * bigP), (1.0 / 6.0 / (halfTan * halfTan)) - cos2P);
        var inverseRa = Math.Sqrt(1 - (12 * halfTan * halfTan * cos2P) + (36 * Math.Pow(halfTan, 4)));

        // The M₁ terms Q and 1/Qₐ with I at its mean value, formulas (203)
        // and (197) of § 122–123, as SP 98's Tables 9 and 10 and NOAA's
        // predictions use them.
        var q = Math.Atan2(0.483 * Math.Sin(bigP), Math.Cos(bigP));
        var inverseQa = Math.Sqrt(2.310 + (1.435 * cos2P));

        var terms = new NodalTerms
        {
            Xi = xi,
            Nu = nu,
            NuPrime = nuPrime,
            TwoNuSecond = twoNuSecond,
            R = r,
            Q = q,
        };

        // The node factors f (§§ 73–78, 149, 207, 215, 227, 235).
        terms.F[(int)NodeFactor.M2] = Math.Pow(halfCos, 4) / 0.9154;
        terms.F[(int)NodeFactor.O1] = sinI * halfCos * halfCos / 0.3800;
        terms.F[(int)NodeFactor.K1] = Math.Sqrt((0.8965 * sin2I * sin2I) + (0.6001 * sin2I * Math.Cos(nu)) + 0.1006);
        terms.F[(int)NodeFactor.K2] = Math.Sqrt((19.0444 * Math.Pow(sinI, 4)) + (2.7702 * sinI * sinI * Math.Cos(2 * nu)) + 0.0981);
        terms.F[(int)NodeFactor.J1] = sin2I / 0.7214;
        terms.F[(int)NodeFactor.OO1] = sinI * Math.Pow(Math.Sin(bigI / 2), 2) / 0.0164;
        terms.F[(int)NodeFactor.Mm] = ((2.0 / 3.0) - (sinI * sinI)) / 0.5021;
        terms.F[(int)NodeFactor.Mf] = sinI * sinI / 0.1578;
        terms.F[(int)NodeFactor.M3] = Math.Pow(halfCos, 6) / 0.8758;
        terms.F[(int)NodeFactor.InverseRa] = inverseRa;
        terms.F[(int)NodeFactor.InverseQa] = inverseQa;
        return terms;
    }
}
