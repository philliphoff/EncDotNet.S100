using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Nmea;

/// <summary>
/// A decoded NMEA 0183 sentence. <see cref="NmeaDecoder.TryDecode(NmeaSentence, out NmeaMessage?)"/> returns
/// one of the derived records for the formatters it knows, whatever the talker.
/// </summary>
/// <remarks>
/// Every value is nullable: NMEA fields may be null (empty), and a value the
/// sentence marks invalid is dropped rather than passed on.
/// </remarks>
public abstract record NmeaMessage
{
    /// <summary>The talker that sent the sentence, e.g. <c>WI</c> (weather instrument) or <c>II</c> (integrated instrumentation).</summary>
    public required string Talker { get; init; }
}

/// <summary>Which wind <see cref="MwvSentence"/> reports.</summary>
public enum WindReference
{
    /// <summary><c>R</c>: relative (apparent) wind, as felt on board.</summary>
    Relative,

    /// <summary><c>T</c>: true (theoretical) wind, computed by the instruments from apparent wind and the vessel's speed, usually through the water.</summary>
    True,
}

/// <summary>
/// <c>MWV</c>, wind speed and angle: the wind angle relative to the bow,
/// clockwise 0° to 360°, and its speed.
/// </summary>
/// <param name="Angle">The angle the wind comes from, clockwise from the bow; <see langword="null"/> when null.</param>
/// <param name="Reference">Relative (apparent) or true.</param>
/// <param name="Speed">The wind speed, converted from the sentence's unit (<c>N</c> knots, <c>M</c> m/s, <c>K</c> km/h, <c>S</c> statute mph); <see langword="null"/> when null or the unit is unknown.</param>
/// <param name="IsValid">The status field: <see langword="false"/> when it is <c>V</c> (data invalid).</param>
public sealed record MwvSentence(Angle? Angle, WindReference Reference, Speed? Speed, bool IsValid) : NmeaMessage;

/// <summary>
/// <c>MWD</c>, wind direction and speed: the true direction the wind blows
/// from, referenced to true and magnetic north, and its speed. Instruments
/// usually compute it over the ground, from apparent wind, heading and GNSS
/// motion.
/// </summary>
/// <param name="TrueDirection">The direction the wind comes from, degrees true.</param>
/// <param name="MagneticDirection">The direction the wind comes from, degrees magnetic.</param>
/// <param name="Speed">The wind speed: the knots field, or the m/s field when knots is null.</param>
public sealed record MwdSentence(Angle? TrueDirection, Angle? MagneticDirection, Speed? Speed) : NmeaMessage;

/// <summary><c>MTW</c>, mean temperature of water.</summary>
/// <param name="Celsius">The water temperature in degrees Celsius; <see langword="null"/> when null or not in <c>C</c>.</param>
public sealed record MtwSentence(double? Celsius) : NmeaMessage;

/// <summary>One measurement of an <see cref="XdrSentence"/>.</summary>
/// <param name="Type">The transducer type, e.g. <c>C</c> temperature, <c>P</c> pressure, <c>H</c> humidity, <c>A</c> angle.</param>
/// <param name="Value">The value; <see langword="null"/> when null.</param>
/// <param name="Units">The units, e.g. <c>C</c> Celsius, <c>B</c> bars, <c>P</c> pascals.</param>
/// <param name="Name">The transducer's name, which the standard leaves to the maker, e.g. <c>AIRTEMP</c>, <c>Barometer</c> or <c>ENV_OUTAIR_T</c>.</param>
public sealed record XdrMeasurement(char Type, double? Value, string Units, string Name);

/// <summary>
/// <c>XDR</c>, transducer measurements: any number of type, value, units and
/// name groups. <see cref="AirTemperatureCelsius"/> and
/// <see cref="PressureHectopascals"/> pick out the weather ones.
/// </summary>
/// <param name="Measurements">The measurements, in the sentence's order.</param>
public sealed record XdrSentence(IReadOnlyList<XdrMeasurement> Measurements) : NmeaMessage
{
    // Names that mark a temperature or pressure as something other than the
    // outside air: engines, tanks, cabins.
    private static readonly string[] NotWeather =
        ["ENGINE", "OIL", "FUEL", "COOLANT", "EXHAUST", "TANK", "HYD", "CABIN", "INSIDE", "ROOM", "FRIDGE", "FREEZER", "BATT", "WATER", "SEA"];

    /// <summary>
    /// The outside air temperature in degrees Celsius: the first <c>C</c>
    /// measurement in <c>C</c> whose name doesn't mark it as something else
    /// (an engine, a tank, a cabin, the sea), between -60 °C and 60 °C.
    /// </summary>
    /// <remarks>
    /// Makers name the transducer as they like (<c>AIRTEMP</c>, <c>TempAir</c>,
    /// <c>ENV_OUTAIR_T</c>, <c>TEMP</c> or nothing), so the name is only used
    /// to rule a reading out, and the range to rule out nonsense.
    /// </remarks>
    public double? AirTemperatureCelsius =>
        Measurements.FirstOrDefault(m =>
            m.Type == 'C'
            && string.Equals(m.Units, "C", StringComparison.OrdinalIgnoreCase)
            && m.Value is >= -60 and <= 60
            && IsWeatherName(m.Name))?.Value;

    /// <summary>
    /// The barometric pressure in hectopascals: the first <c>P</c> measurement
    /// in bars (<c>B</c>) or pascals (<c>P</c>) whose name doesn't mark it as
    /// something else (oil, fuel, a tank), between 850 hPa and 1,100 hPa.
    /// </summary>
    /// <remarks>
    /// The range keeps engine and tank pressures out even when their names
    /// don't. Some senders mislabel hectopascals as pascals; a value in the
    /// range as given is taken as hectopascals.
    /// </remarks>
    public double? PressureHectopascals
    {
        get
        {
            foreach (var m in Measurements)
            {
                if (m.Type != 'P' || m.Value is not { } v || !IsWeatherName(m.Name))
                    continue;

                double? hpa = m.Units.ToUpperInvariant() switch
                {
                    "B" => v * 1000,
                    "P" => v is >= 850 and <= 1100 ? v : v / 100,
                    _ => null,
                };
                if (hpa is >= 850 and <= 1100)
                    return hpa;
            }
            return null;
        }
    }

    private static bool IsWeatherName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return true;
        var upper = name.ToUpperInvariant();
        if (upper.Contains("AIR", StringComparison.Ordinal) || upper.Contains("BARO", StringComparison.Ordinal) || upper.Contains("ATMOS", StringComparison.Ordinal))
            return true;
        foreach (var word in NotWeather)
        {
            if (upper.Contains(word, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

/// <summary><c>HDT</c>, heading true.</summary>
/// <param name="Heading">The heading, degrees true.</param>
public sealed record HdtSentence(Angle? Heading) : NmeaMessage;

/// <summary><c>HDG</c>, heading, deviation and variation.</summary>
/// <param name="MagneticSensorHeading">The magnetic sensor heading, degrees.</param>
/// <param name="Deviation">The compass deviation, east positive.</param>
/// <param name="Variation">The magnetic variation, east positive.</param>
public sealed record HdgSentence(Angle? MagneticSensorHeading, Angle? Deviation, Angle? Variation) : NmeaMessage
{
    /// <summary>
    /// The true heading: sensor heading plus deviation plus variation, when
    /// the sensor heading and the variation are known (a null deviation counts
    /// as zero, as the compass may already be corrected).
    /// </summary>
    public Angle? TrueHeading => MagneticSensorHeading is { } h && Variation is { } v
        ? NmeaAngles.Normalise(h + (Deviation ?? Angle.Zero) + v)
        : null;
}

/// <summary><c>VHW</c>, water speed and heading.</summary>
/// <param name="TrueHeading">The heading, degrees true.</param>
/// <param name="MagneticHeading">The heading, degrees magnetic.</param>
/// <param name="SpeedThroughWater">The speed through the water: the knots field, or the km/h field when knots is null.</param>
public sealed record VhwSentence(Angle? TrueHeading, Angle? MagneticHeading, Speed? SpeedThroughWater) : NmeaMessage;

/// <summary><c>RMC</c>, recommended minimum specific GNSS data.</summary>
/// <param name="Time">The fix time (UTC) from the time and date fields, or <see langword="null"/> when either is null.</param>
/// <param name="Position">The position, or <see langword="null"/> when null or the fix is invalid.</param>
/// <param name="SpeedOverGround">The speed over ground, or <see langword="null"/> when null or the fix is invalid.</param>
/// <param name="CourseOverGround">The course over ground, degrees true, or <see langword="null"/> when null or the fix is invalid.</param>
/// <param name="MagneticVariation">The magnetic variation, east positive.</param>
/// <param name="IsValid">Whether the status is <c>A</c> and the mode indicator (when present) isn't <c>N</c>.</param>
public sealed record RmcSentence(
    DateTimeOffset? Time,
    GeoPosition? Position,
    Speed? SpeedOverGround,
    Angle? CourseOverGround,
    Angle? MagneticVariation,
    bool IsValid) : NmeaMessage;

/// <summary><c>VTG</c>, course over ground and ground speed.</summary>
/// <param name="CourseOverGround">The course over ground, degrees true.</param>
/// <param name="SpeedOverGround">The speed over ground: the knots field, or the km/h field when knots is null.</param>
/// <param name="IsValid">Whether the mode indicator (when present) isn't <c>N</c>.</param>
public sealed record VtgSentence(Angle? CourseOverGround, Speed? SpeedOverGround, bool IsValid) : NmeaMessage;
