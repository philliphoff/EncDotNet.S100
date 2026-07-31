
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.DynamicSources;
/// <summary>
/// Optional motion sidecar for a moving point feature (own-ship, AIS
/// target). Static features (waypoints, weather contours, sensor
/// readings) leave <see cref="DynamicFeature.Motion"/> <see langword="null"/>.
/// </summary>
/// <remarks>
/// Angles follow marine convention (clockwise from true north). Each field is
/// nullable because real-world feeds frequently report a subset
/// (e.g. an AIS Class B "still" report typically lacks heading).
/// </remarks>
public sealed record DynamicMotion
{
    /// <summary>Course over ground, true (clockwise from north).</summary>
    public Angle? CourseOverGround { get; init; }

    /// <summary>True heading (clockwise from north).</summary>
    public Angle? Heading { get; init; }

    /// <summary>Speed over ground.</summary>
    public Speed? SpeedOverGround { get; init; }
}
