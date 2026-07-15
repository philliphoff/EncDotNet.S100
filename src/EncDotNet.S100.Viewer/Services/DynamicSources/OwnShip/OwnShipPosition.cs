using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// One own-ship position fix published by an
/// <see cref="IOwnShipPositionProvider"/>. Geometry-agnostic record;
/// adapted into a
/// <c>EncDotNet.S100.DynamicSources.DynamicFeature</c> by
/// <see cref="OwnShipSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Motion is modelled with strongly typed quantities
/// (<see cref="Angle"/>, <see cref="Speed"/>) so the unit is carried
/// by the value rather than the field name — <see cref="OwnShipSource"/>
/// forwards them straight onto <c>DynamicMotion</c> with no conversion.
/// </para>
/// <para>
/// All motion fields are nullable so that a stationary or
/// motion-less provider can publish position alone. When both
/// <see cref="CourseOverGround"/> and <see cref="SpeedOverGround"/>
/// are <see langword="null"/>, the source emits a
/// <c>DynamicFeature</c> with no motion sidecar and the default
/// renderer omits the predictor line.
/// </para>
/// </remarks>
/// <param name="Latitude">WGS-84 latitude in decimal degrees.</param>
/// <param name="Longitude">WGS-84 longitude in decimal degrees.</param>
/// <param name="CourseOverGround">
/// Course over ground (degrees true, 0–360), or
/// <see langword="null"/> when no course is known.
/// </param>
/// <param name="SpeedOverGround">
/// Speed over ground, or <see langword="null"/> when no speed is
/// known.
/// </param>
/// <param name="Timestamp">UTC instant the fix was observed.</param>
/// <param name="Heading">
/// Gyro/true heading (degrees true, 0–360), or
/// <see langword="null"/> when no separate heading is known — in which
/// case <see cref="OwnShipSource"/> falls back to mirroring
/// <see cref="CourseOverGround"/>. Kept distinct from course so a
/// driver that knows both (a real gyro, or an impersonated AIS target
/// reporting heading independently of COG) orients the true-scale hull
/// correctly.
/// </param>
internal sealed record OwnShipPosition(
    double Latitude,
    double Longitude,
    Angle? CourseOverGround,
    Speed? SpeedOverGround,
    DateTimeOffset Timestamp,
    Angle? Heading = null);
