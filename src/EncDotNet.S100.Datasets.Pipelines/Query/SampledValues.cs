using System.ComponentModel;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

// The typed S-102, S-104 and S-111 sample shapes. Their JSON is the
// sample_coverage contract for those products; GetFields describes the same
// values generically, so a caller can treat them like any product's
// CoverageFieldsSample.

/// <summary>S-102 depth sample (metres below the vertical datum, positive down).</summary>
/// <param name="DepthMeters">Depth in metres below the vertical datum, positive down (per S-102).</param>
/// <param name="UncertaintyMeters">Vertical uncertainty in metres at the resolved cell; null when the source dataset has no uncertainty layer.</param>
public sealed record DepthSample(
    [property: Description("Depth in metres below the vertical datum, positive down (per S-102).")] double DepthMeters,
    [property: Description("Vertical uncertainty in metres at the resolved cell; null when the source dataset has no uncertainty layer.")] double? UncertaintyMeters) : SampledValue
{
    /// <inheritdoc />
    public override IReadOnlyList<SampledField> GetFields() =>
    [
        new("depth", "depth", "m", DepthMeters),
        new("uncertainty", "vertical_uncertainty", "m", UncertaintyMeters),
    ];
}

/// <summary>
/// S-104 water level sample read from a dcf8 ("time series at fixed
/// stations") dataset — picks the nearest station to the requested
/// position and the nearest time step within that station's series.
/// </summary>
/// <param name="StationId">Reporting station identifier (S-104 <c>stationIdentification</c>).</param>
/// <param name="StationDistanceMetres">Great-circle distance from requested point to the station, metres.</param>
/// <param name="WaterLevelHeight">Water level height in metres relative to the vertical datum.</param>
/// <param name="Trend">Decoded S-104 trend (see <see cref="WaterLevelSample"/>).</param>
/// <param name="SampleTime">Actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="StationLatitude">Latitude of the matched station.</param>
/// <param name="StationLongitude">Longitude of the matched station.</param>
public sealed record WaterLevelStationSample(
    [property: Description("Reporting station identifier (S-104 stationIdentification).")] string StationId,
    [property: Description("Great-circle distance from requested point to the station, in metres.")] double StationDistanceMetres,
    [property: Description("Water level height in metres relative to the vertical datum, positive up (per S-104).")] double WaterLevelHeight,
    [property: Description("S-104 decoded trend: \"decreasing\", \"increasing\", \"steady\", \"unknown\" (S-104 §10.2.2). Raw integer surfaced as a string for unrecognised values.")] string Trend,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Latitude of the matched station, decimal degrees, WGS-84.")] double StationLatitude,
    [property: Description("Longitude of the matched station, decimal degrees, WGS-84.")] double StationLongitude) : SampledValue
{
    /// <inheritdoc />
    public override IReadOnlyList<SampledField> GetFields() => SampledFields.WaterLevel(WaterLevelHeight, Trend);
}

/// <summary>
/// S-104 water level sample at the nearest grid cell and time step.
/// </summary>
/// <param name="WaterLevelHeight">Water level height in metres relative to the vertical datum.</param>
/// <param name="Trend">
/// Decoded S-104 trend (per S-104 §10.2.2: <c>0=unknown</c>, <c>1=decreasing</c>,
/// <c>2=increasing</c>, <c>3=steady</c>). When the raw value falls outside the
/// spec-defined set the raw integer is surfaced as the string instead.
/// </param>
/// <param name="SampleTime">The actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="Row">Row index of the resolved cell in the source grid (0-based).</param>
/// <param name="Column">Column index of the resolved cell in the source grid (0-based).</param>
/// <param name="CellCentreLatitude">Latitude of the resolved cell centre.</param>
/// <param name="CellCentreLongitude">Longitude of the resolved cell centre.</param>
public sealed record WaterLevelSample(
    [property: Description("Water level height in metres relative to the vertical datum, positive up (per S-104).")] double WaterLevelHeight,
    [property: Description("S-104 decoded trend: \"decreasing\", \"increasing\", \"steady\", \"unknown\" (S-104 §10.2.2). Raw integer surfaced as a string for unrecognised values.")] string Trend,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Zero-based row index of the resolved cell in the source grid.")] int Row,
    [property: Description("Zero-based column index of the resolved cell in the source grid.")] int Column,
    [property: Description("Latitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLatitude,
    [property: Description("Longitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLongitude) : SampledValue
{
    /// <inheritdoc />
    public override IReadOnlyList<SampledField> GetFields() => SampledFields.WaterLevel(WaterLevelHeight, Trend);
}

/// <summary>
/// S-111 surface current sample at the nearest grid cell and time step.
/// </summary>
/// <param name="SpeedMetresPerSecond">
/// Speed in metres per second, converted from the encoded knots as
/// <c>kn × 0.514444</c>.
/// </param>
/// <param name="SpeedKnots">
/// Speed in knots — the unit S-111 encodes <c>surfaceCurrentSpeed</c> in.
/// </param>
/// <param name="DirectionDegreesTrue">Direction in degrees from true north, clockwise (0..360).</param>
/// <param name="SampleTime">The actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="Row">Row index of the resolved cell in the source grid (0-based).</param>
/// <param name="Column">Column index of the resolved cell in the source grid (0-based).</param>
/// <param name="CellCentreLatitude">Latitude of the resolved cell centre.</param>
/// <param name="CellCentreLongitude">Longitude of the resolved cell centre.</param>
public sealed record SurfaceCurrentSample(
    [property: Description("Current speed in metres per second, converted from the S-111 encoded knots (kn × 0.514444).")] double SpeedMetresPerSecond,
    [property: Description("Current speed in knots, as encoded by S-111 surfaceCurrentSpeed.")] double SpeedKnots,
    [property: Description("Direction the current is flowing toward, in degrees from true north, clockwise, 0..360.")] double DirectionDegreesTrue,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Zero-based row index of the resolved cell in the source grid.")] int Row,
    [property: Description("Zero-based column index of the resolved cell in the source grid.")] int Column,
    [property: Description("Latitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLatitude,
    [property: Description("Longitude of the resolved cell centre, decimal degrees, WGS-84.")] double CellCentreLongitude) : SampledValue
{
    /// <inheritdoc />
    public override IReadOnlyList<SampledField> GetFields() => SampledFields.SurfaceCurrent(SpeedKnots, DirectionDegreesTrue);
}

/// <summary>
/// S-111 surface current sample read from a dcf8 ("time series at
/// fixed stations") dataset — picks the nearest station to the
/// requested position and the nearest time step within that station's
/// series (S-111 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
/// <param name="StationId">Reporting station identifier (S-111 <c>stationIdentification</c>).</param>
/// <param name="StationDistanceMetres">Great-circle distance from requested point to the station, metres.</param>
/// <param name="SpeedMetresPerSecond">Speed in metres per second, converted from the encoded knots as <c>kn × 0.514444</c>.</param>
/// <param name="SpeedKnots">Speed in knots — the unit S-111 encodes <c>surfaceCurrentSpeed</c> in.</param>
/// <param name="DirectionDegreesTrue">Direction in degrees from true north, clockwise (0..360).</param>
/// <param name="SampleTime">Actual time step (UTC) selected for this sample.</param>
/// <param name="RequestedTime">The time the caller asked for, or <c>null</c> if unspecified.</param>
/// <param name="StationLatitude">Latitude of the matched station.</param>
/// <param name="StationLongitude">Longitude of the matched station.</param>
public sealed record SurfaceCurrentStationSample(
    [property: Description("Reporting station identifier (S-111 stationIdentification).")] string StationId,
    [property: Description("Great-circle distance from requested point to the station, in metres.")] double StationDistanceMetres,
    [property: Description("Current speed in metres per second, converted from the S-111 encoded knots (kn × 0.514444).")] double SpeedMetresPerSecond,
    [property: Description("Current speed in knots, as encoded by S-111 surfaceCurrentSpeed.")] double SpeedKnots,
    [property: Description("Direction the current is flowing toward, in degrees from true north, clockwise, 0..360.")] double DirectionDegreesTrue,
    [property: Description("UTC instant of the time step actually selected for this sample.")] DateTime SampleTime,
    [property: Description("UTC instant the caller asked for, or null if unspecified.")] DateTimeOffset? RequestedTime,
    [property: Description("Latitude of the matched station, decimal degrees, WGS-84.")] double StationLatitude,
    [property: Description("Longitude of the matched station, decimal degrees, WGS-84.")] double StationLongitude) : SampledValue
{
    /// <inheritdoc />
    public override IReadOnlyList<SampledField> GetFields() => SampledFields.SurfaceCurrent(SpeedKnots, DirectionDegreesTrue);
}

/// <summary>The <see cref="SampledField"/> lists shared by the typed sample shapes.</summary>
internal static class SampledFields
{
    public static IReadOnlyList<SampledField> WaterLevel(double height, string trend) =>
    [
        new("waterLevelHeight", "water_level_height", "m", height),
        new("waterLevelTrend", "water_level_trend", null, null, trend),
    ];

    public static IReadOnlyList<SampledField> SurfaceCurrent(double speedKnots, double directionDegreesTrue) =>
    [
        new("surfaceCurrentSpeed", "current_speed", "kn", speedKnots),
        new("surfaceCurrentDirection", "current_direction", "deg", directionDegreesTrue),
    ];
}
