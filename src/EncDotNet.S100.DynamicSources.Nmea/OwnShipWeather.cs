using EncDotNet.S100.DataModel;
using EncDotNet.S100.Nmea;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.DynamicSources.Nmea;

/// <summary>Where an <see cref="OwnShipWeather.TrueWind"/> came from, best first.</summary>
public enum TrueWindSource
{
    /// <summary>No true wind: only the relative (apparent) wind is known, or no wind at all.</summary>
    None,

    /// <summary>The instruments' <c>MWD</c>: true direction and speed, usually over the ground.</summary>
    Mwd,

    /// <summary>
    /// Derived here from the apparent wind (<c>MWV</c> R), the heading and the
    /// speed and course over the ground: the wind a buoy or a forecast gives.
    /// </summary>
    DerivedOverGround,

    /// <summary>The instruments' <c>MWV</c> with <c>T</c>, turned into a direction with the heading: usually through the water.</summary>
    MwvTrue,

    /// <summary>Derived here from the apparent wind, the heading and the speed through the water (<c>VHW</c>).</summary>
    DerivedThroughWater,
}

/// <summary>
/// The weather at own ship, as the latest NMEA 0183 sentences give it. Every
/// value is optional; temperatures and pressure are plain numbers in the units
/// their names give, like <c>NdbcObservation</c>'s.
/// </summary>
public sealed record OwnShipWeather
{
    /// <summary>The time of the newest value, as the source received it.</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>The true wind, or <see langword="null"/> when it can't be had (see <see cref="TrueWindSource"/>).</summary>
    public WindVector? TrueWind { get; init; }

    /// <summary>Where <see cref="TrueWind"/> came from.</summary>
    public TrueWindSource TrueWindSource { get; init; }

    /// <summary>The relative (apparent) wind angle, clockwise from the bow, from <c>MWV</c> R.</summary>
    public Angle? RelativeWindAngle { get; init; }

    /// <summary>The relative (apparent) wind speed, from <c>MWV</c> R.</summary>
    public Speed? RelativeWindSpeed { get; init; }

    /// <summary>
    /// The direction the relative wind comes from, degrees true: heading plus
    /// <see cref="RelativeWindAngle"/>, when the heading is known. Drawn when
    /// there is no true wind.
    /// </summary>
    public Angle? RelativeWindFromDirection { get; init; }

    /// <summary>The heading used, degrees true.</summary>
    public Angle? Heading { get; init; }

    /// <summary>The water temperature in °C, from <c>MTW</c>.</summary>
    public double? WaterTemperatureCelsius { get; init; }

    /// <summary>The air temperature in °C, from <c>XDR</c>.</summary>
    public double? AirTemperatureCelsius { get; init; }

    /// <summary>The barometric pressure in hPa, from <c>XDR</c>.</summary>
    public double? PressureHectopascals { get; init; }

    /// <summary>Whether only the relative wind is known: the barb, if any, shows the relative wind and is labelled so.</summary>
    public bool IsRelativeOnly => TrueWind is null && RelativeWindSpeed is not null;

    /// <summary>Whether there is anything to show.</summary>
    public bool HasAnyValue =>
        TrueWind is not null || RelativeWindSpeed is not null || WaterTemperatureCelsius is not null
        || AirTemperatureCelsius is not null || PressureHectopascals is not null;
}

/// <summary>
/// Own ship as the host knows it: where the weather feature is drawn, and the
/// motion used to derive true wind when the NMEA stream has none of its own.
/// </summary>
/// <param name="Position">The own-ship position.</param>
/// <param name="Heading">The heading, degrees true, or <see langword="null"/>.</param>
/// <param name="CourseOverGround">The course over the ground, degrees true, or <see langword="null"/>.</param>
/// <param name="SpeedOverGround">The speed over the ground, or <see langword="null"/>.</param>
/// <param name="Timestamp">When the fix was made.</param>
public sealed record OwnShipReference(
    GeoPosition Position,
    Angle? Heading,
    Angle? CourseOverGround,
    Speed? SpeedOverGround,
    DateTimeOffset Timestamp);
