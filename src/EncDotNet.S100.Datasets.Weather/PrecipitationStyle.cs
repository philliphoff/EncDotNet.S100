using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The precipitation rate (<see cref="WeatherParameters.PrecipitationRate"/>)
/// as a colour fill in the rain radar's ramp, from light (0.1 mm/h) to
/// violent (50 mm/h and more); where it's drier than 0.1 mm/h nothing is
/// drawn. Off by default.
/// </summary>
/// <remarks>
/// <para>
/// The bands (<see cref="Bands"/>) follow the usual rain intensity terms:
/// light under 2.5 mm/h (split at 0.5 mm/h, so drizzle and showers show
/// apart), moderate to 7.5 mm/h, heavy to 15 mm/h, very heavy to 50 mm/h,
/// and violent beyond. The field is stored in kg m⁻² s⁻¹, which is
/// millimetres of water a second (<see cref="WeatherUnit.KilogramsPerSquareMetrePerSecond"/>);
/// the bands are labelled in millimetres an hour, the unit radar and
/// forecasts give it in (<see cref="ToMillimetresPerHour"/>).
/// </para>
/// <para>
/// Colours are the rain radar ramp of <see cref="WeatherPalette"/>, drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>. The fill is clipped to
/// water when an S-101 chart is loaded
/// (<see cref="WeatherCoverageStyle.ClipToWater"/>), so the land chart stays
/// clear.
/// </para>
/// </remarks>
public sealed class PrecipitationStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.precipitation"</c>.</summary>
    public const string Key = "weather.precipitation";

    /// <summary>The rate under which nothing is drawn: 0.1 mm/h.</summary>
    public const double LightestMillimetresPerHour = 0.1;

    /// <summary>The bands, lightest rain first, in kg m⁻² s⁻¹.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } =
    [
        Band(LightestMillimetresPerHour, 0.5, 1, "Light, 0.1 to 0.5 mm/h"),
        Band(0.5, 2.5, 2, "Light, 0.5 to 2.5 mm/h"),
        Band(2.5, 7.5, 3, "Moderate, 2.5 to 7.5 mm/h"),
        Band(7.5, 15, 4, "Heavy, 7.5 to 15 mm/h"),
        Band(15, 50, 5, "Very heavy, 15 to 50 mm/h"),
        Band(50, double.PositiveInfinity, 6, "Violent, 50 mm/h and more"),
    ];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public PrecipitationStyle()
        : base(Key, "Precipitation", WeatherLayerKind.Fill, WeatherParameters.PrecipitationRate, null,
            isVisibleByDefault: false, clipToWater: true)
    {
    }

    /// <summary>Converts a precipitation rate in kg m⁻² s⁻¹ (mm/s) to millimetres an hour.</summary>
    /// <param name="kilogramsPerSquareMetrePerSecond">The rate as stored.</param>
    public static double ToMillimetresPerHour(double kilogramsPerSquareMetrePerSecond) => kilogramsPerSquareMetrePerSecond * 3600;

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);

    private static WeatherBand Band(double fromMillimetresPerHour, double toMillimetresPerHour, int band, string label) =>
        new((float)(fromMillimetresPerHour / 3600), (float)(toMillimetresPerHour / 3600), WeatherPalette.PrecipitationToken(band), label);
}
