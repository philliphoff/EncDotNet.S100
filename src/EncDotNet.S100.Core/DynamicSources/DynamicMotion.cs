namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Optional motion sidecar for a moving point feature (own-ship, AIS
/// target). Static features (waypoints, weather contours, sensor
/// readings) leave <see cref="DynamicFeature.Motion"/> <see langword="null"/>.
/// </summary>
/// <remarks>
/// Angles are expressed in degrees, clockwise from true north,
/// matching marine convention. Speed is in knots. Each field is
/// nullable because real-world feeds frequently report a subset
/// (e.g. an AIS Class B "still" report typically lacks heading).
/// </remarks>
public sealed record DynamicMotion
{
    /// <summary>Course over ground in degrees true (0–360).</summary>
    public double? CourseOverGroundDeg { get; init; }

    /// <summary>True heading in degrees (0–360).</summary>
    public double? HeadingDeg { get; init; }

    /// <summary>Speed over ground in knots.</summary>
    public double? SpeedOverGroundKn { get; init; }
}
