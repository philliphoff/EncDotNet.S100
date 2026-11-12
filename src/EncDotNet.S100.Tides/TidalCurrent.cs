namespace EncDotNet.S100.Tides;

/// <summary>The predicted tidal current at a time.</summary>
/// <param name="VelocityMetersPerSecond">
/// The velocity along the major axis, in metres per second: positive while
/// the current floods, negative while it ebbs.
/// </param>
/// <param name="SpeedMetersPerSecond">The current's speed, in metres per second.</param>
/// <param name="DirectionDegrees">
/// The direction the current sets toward, in degrees true, from 0 up to
/// 360: the flood or ebb direction for a rectilinear current, or the
/// direction of the predicted vector for a rotary one.
/// </param>
public readonly record struct TidalCurrent(double VelocityMetersPerSecond, double SpeedMetersPerSecond, double DirectionDegrees)
{
    /// <summary>Metres per second in one knot.</summary>
    public const double MetersPerSecondPerKnot = 1852.0 / 3600.0;

    /// <summary>Whether the current is flooding: its velocity along the major axis is positive.</summary>
    public bool IsFlood => VelocityMetersPerSecond > 0;

    /// <summary>The current's speed, in knots.</summary>
    public double SpeedKnots => SpeedMetersPerSecond / MetersPerSecondPerKnot;
}
