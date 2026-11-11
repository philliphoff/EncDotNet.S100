using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Nmea;

/// <summary>A wind: the direction it blows from, degrees true, and its speed.</summary>
/// <param name="FromDirection">The direction the wind comes from, degrees true, 0° to 360°. 0° for a calm.</param>
/// <param name="Speed">The wind speed.</param>
public readonly record struct WindVector(Angle FromDirection, Speed Speed);

/// <summary>
/// True wind from apparent wind: the wind felt on board, plus the vessel's own
/// motion.
/// </summary>
/// <remarks>
/// <para>
/// The apparent wind is the true wind minus the vessel's velocity. So the true
/// wind velocity is the apparent wind velocity (from heading plus the apparent
/// angle) plus the vessel's velocity. Which velocity decides what "true"
/// means:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Over the ground</b> (<see cref="OverGround"/>): speed and course over the
/// ground from GNSS. This is the wind a buoy or a forecast gives, so it is the
/// one to set beside them. Instruments usually report it as <c>MWD</c>.
/// </description></item>
/// <item><description>
/// <b>Through the water</b> (<see cref="ThroughWater"/>): speed through the
/// water along the heading, from the log. This is the sailor's true wind
/// (instruments' <c>MWV</c> with <c>T</c>); in a current it differs from the
/// ground wind by the current.
/// </description></item>
/// </list>
/// <para>
/// Both ignore the mast's motion, upwash and leeway: instruments that correct
/// for those report their own true wind, which a caller should prefer.
/// </para>
/// </remarks>
public static class TrueWind
{
    /// <summary>The true wind over the ground.</summary>
    /// <param name="apparentAngle">The apparent wind angle, clockwise from the bow (<c>MWV</c> with <c>R</c>).</param>
    /// <param name="apparentSpeed">The apparent wind speed.</param>
    /// <param name="heading">The heading, degrees true.</param>
    /// <param name="courseOverGround">The course over the ground, degrees true.</param>
    /// <param name="speedOverGround">The speed over the ground.</param>
    public static WindVector OverGround(Angle apparentAngle, Speed apparentSpeed, Angle heading, Angle courseOverGround, Speed speedOverGround) =>
        FromApparent(apparentAngle, apparentSpeed, heading, courseOverGround, speedOverGround);

    /// <summary>The true wind through the water: the vessel moves along its heading at its speed through the water.</summary>
    /// <param name="apparentAngle">The apparent wind angle, clockwise from the bow (<c>MWV</c> with <c>R</c>).</param>
    /// <param name="apparentSpeed">The apparent wind speed.</param>
    /// <param name="heading">The heading, degrees true.</param>
    /// <param name="speedThroughWater">The speed through the water.</param>
    public static WindVector ThroughWater(Angle apparentAngle, Speed apparentSpeed, Angle heading, Speed speedThroughWater) =>
        FromApparent(apparentAngle, apparentSpeed, heading, heading, speedThroughWater);

    /// <summary>The true wind for a vessel moving along <paramref name="course"/> at <paramref name="speed"/>.</summary>
    /// <param name="apparentAngle">The apparent wind angle, clockwise from the bow.</param>
    /// <param name="apparentSpeed">The apparent wind speed.</param>
    /// <param name="heading">The heading, degrees true: where the bow points, which the apparent angle is measured from.</param>
    /// <param name="course">The direction the vessel moves in, degrees true.</param>
    /// <param name="speed">The speed the vessel moves at.</param>
    public static WindVector FromApparent(Angle apparentAngle, Speed apparentSpeed, Angle heading, Angle course, Speed speed)
    {
        // East and north components of the air's velocity (where it blows to).
        double apparentFrom = (heading + apparentAngle).TotalRadians;
        double aws = apparentSpeed.TotalMetresPerSecond;
        double east = -aws * Math.Sin(apparentFrom);
        double north = -aws * Math.Cos(apparentFrom);

        double c = course.TotalRadians, v = speed.TotalMetresPerSecond;
        east += v * Math.Sin(c);
        north += v * Math.Cos(c);

        double tws = Math.Sqrt(east * east + north * north);
        if (tws < 1e-9)
            return new WindVector(Angle.Zero, Speed.Zero);

        // The wind comes from the opposite of where it blows to.
        var from = Angle.FromRadians(Math.Atan2(-east, -north));
        return new WindVector(NmeaAngles.Normalise(from), Speed.FromMetresPerSecond(tws));
    }
}

/// <summary>Angle helpers shared by the decoders.</summary>
public static class NmeaAngles
{
    /// <summary>Wraps <paramref name="angle"/> into 0° to 360° (360° excluded).</summary>
    /// <param name="angle">The angle.</param>
    public static Angle Normalise(Angle angle)
    {
        double d = angle.TotalDegrees % 360;
        if (d < 0)
            d += 360;
        // -0.0000001 % 360 + 360 can round to 360.
        return Angle.FromDegrees(d >= 360 ? 0 : d);
    }
}
