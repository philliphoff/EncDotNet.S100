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
/// <see cref="SpeedOverGroundMs"/> is in metres per second — the SI
/// convention used internally throughout the viewer's dynamic-source
/// stack. <see cref="OwnShipSource"/> converts to knots when
/// populating the public
/// <c>DynamicMotion.SpeedOverGroundKn</c> field (which is the
/// contractual unit on that record).
/// </para>
/// <para>
/// All motion fields are nullable so that a stationary or
/// motion-less provider can publish position alone. When both
/// <see cref="CourseOverGroundDeg"/> and <see cref="SpeedOverGroundMs"/>
/// are <see langword="null"/>, the source emits a
/// <c>DynamicFeature</c> with no motion sidecar and the default
/// renderer omits the predictor line.
/// </para>
/// </remarks>
/// <param name="Latitude">WGS-84 latitude in decimal degrees.</param>
/// <param name="Longitude">WGS-84 longitude in decimal degrees.</param>
/// <param name="CourseOverGroundDeg">
/// Course over ground in degrees true (0–360), or
/// <see langword="null"/> when no course is known.
/// </param>
/// <param name="SpeedOverGroundMs">
/// Speed over ground in metres per second, or
/// <see langword="null"/> when no speed is known.
/// </param>
/// <param name="Timestamp">UTC instant the fix was observed.</param>
internal sealed record OwnShipPosition(
    double Latitude,
    double Longitude,
    double? CourseOverGroundDeg,
    double? SpeedOverGroundMs,
    DateTimeOffset Timestamp);
