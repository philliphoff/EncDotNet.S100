using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The freezing spray (vessel icing) risk: Overland's icing predictor from
/// the wind speed, air temperature and sea temperature, and its classes.
/// </summary>
/// <remarks>
/// <para>
/// The predictor is Overland's (Overland, Pease, Preisendorfer and Comiskey,
/// 1986; Overland, 1990):
/// <c>PPR = Va (Tf − Ta) / (1 + 0.3 (Tw − Tf))</c>, with <c>Va</c> the wind
/// speed in metres per second, <c>Ta</c> the air temperature and <c>Tw</c>
/// the sea temperature in degrees Celsius, and <c>Tf</c> the freezing point
/// of sea water, −1.7 °C (<see cref="FreezingPointCelsius"/>). It's the
/// predictor NOAA's Ocean Prediction Center and the US National Weather
/// Service forecast freezing spray with.
/// </para>
/// <para>
/// The classes follow Overland (1990): none at or below 0, light to 22.4,
/// moderate to 53.3, heavy to 83.0 and extreme above it, which are icing
/// rates of about 0.7, 2 and 4 cm per hour at the class limits for a
/// 20–75 m fishing vessel.
/// </para>
/// <para>
/// A sea temperature below −2 °C isn't open water (it's sea ice, or the
/// ground when a model's skin temperature stands in for the sea's), so it
/// gives no predictor (<see cref="float.NaN"/>), as does a cell over land.
/// </para>
/// </remarks>
public static class FreezingSpray
{
    /// <summary>The freezing point of sea water used by the predictor: −1.7 °C.</summary>
    public const double FreezingPointCelsius = -1.7;

    /// <summary>The coldest sea temperature taken as open water: −2 °C.</summary>
    public const double ColdestWaterCelsius = -2;

    /// <summary>The upper limit of light icing: a predictor of 22.4.</summary>
    public const double LightLimit = 22.4;

    /// <summary>The upper limit of moderate icing: a predictor of 53.3.</summary>
    public const double ModerateLimit = 53.3;

    /// <summary>The upper limit of heavy icing: a predictor of 83.0. Anything higher is extreme.</summary>
    public const double HeavyLimit = 83.0;

    /// <summary>
    /// Overland's icing predictor for a wind speed, air temperature and sea
    /// temperature.
    /// </summary>
    /// <param name="windSpeed">The wind speed.</param>
    /// <param name="airCelsius">The air temperature, degrees Celsius.</param>
    /// <param name="waterCelsius">The sea temperature, degrees Celsius.</param>
    /// <returns>
    /// The predictor: 0 or less when there's no icing, or
    /// <see cref="double.NaN"/> when the water is colder than
    /// <see cref="ColdestWaterCelsius"/> or an input isn't a number.
    /// </returns>
    public static double Predictor(Speed windSpeed, double airCelsius, double waterCelsius)
    {
        double wind = windSpeed.TotalMetresPerSecond;
        if (!double.IsFinite(wind) || !double.IsFinite(airCelsius) || !double.IsFinite(waterCelsius) || waterCelsius < ColdestWaterCelsius)
            return double.NaN;
        return wind * (FreezingPointCelsius - airCelsius) / (1 + 0.3 * (waterCelsius - FreezingPointCelsius));
    }

    /// <summary>The icing class of a predictor.</summary>
    /// <param name="predictor">A predictor from <see cref="Predictor"/>.</param>
    /// <returns>The class, or <see langword="null"/> when the predictor isn't a number.</returns>
    public static FreezingSprayClass? Classify(double predictor) => predictor switch
    {
        double.NaN => null,
        <= 0 => FreezingSprayClass.None,
        < LightLimit => FreezingSprayClass.Light,
        < ModerateLimit => FreezingSprayClass.Moderate,
        < HeavyLimit => FreezingSprayClass.Heavy,
        _ => FreezingSprayClass.Extreme,
    };

    /// <summary>The display name of a class: "None", "Light", "Moderate", "Heavy" or "Extreme".</summary>
    /// <param name="value">The class.</param>
    public static string GetLabel(FreezingSprayClass value) => value switch
    {
        FreezingSprayClass.Light => "Light",
        FreezingSprayClass.Moderate => "Moderate",
        FreezingSprayClass.Heavy => "Heavy",
        FreezingSprayClass.Extreme => "Extreme",
        _ => "None",
    };

    // The predictor from wind speed (m/s), air and water temperature (K), and
    // optionally a land cover mask: NaN over land.
    internal static float PredictorCell(float windMetresPerSecond, float airKelvin, float waterKelvin, float land)
    {
        if (land >= 0.5f)
            return float.NaN;
        return (float)Predictor(
            Speed.FromMetresPerSecond(windMetresPerSecond),
            airKelvin - SeaTemperatureStyle.CelsiusZero,
            waterKelvin - SeaTemperatureStyle.CelsiusZero);
    }
}

/// <summary>A freezing spray (vessel icing) class (<see cref="FreezingSpray.Classify"/>).</summary>
public enum FreezingSprayClass
{
    /// <summary>No icing: the air isn't colder than the freezing point of sea water, or there's no wind.</summary>
    None,

    /// <summary>Light icing: a predictor under 22.4, below about 0.7 cm per hour.</summary>
    Light,

    /// <summary>Moderate icing: a predictor from 22.4 to 53.3, about 0.7 to 2 cm per hour.</summary>
    Moderate,

    /// <summary>Heavy icing: a predictor from 53.3 to 83.0, about 2 to 4 cm per hour.</summary>
    Heavy,

    /// <summary>Extreme icing: a predictor of 83.0 or more, over about 4 cm per hour.</summary>
    Extreme,
}
