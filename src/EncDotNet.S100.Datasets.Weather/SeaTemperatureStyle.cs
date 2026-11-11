using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The sea surface temperature (<c>sea.temperature</c>) as a colour fill in
/// 1 °C bands from 0 °C to 30 °C, with a band below and a band above. On by
/// default.
/// </summary>
/// <remarks>
/// <para>
/// One-degree bands show the fronts mariners read SST for: the Gulf Stream's
/// north wall, eddies and upwelling edges. The bands are fixed, so the same
/// colour means the same temperature on every chart and in every dataset.
/// </para>
/// <para>
/// The field is stored in kelvin (<see cref="WeatherUnit.Kelvin"/>); the
/// bands are defined and labelled in degrees Celsius. Colours are the
/// diverging ramp of <see cref="WeatherPalette"/>, drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>. SST is a sea quantity, so
/// the fill is clipped to water when an S-101 chart is loaded
/// (<see cref="WeatherCoverageStyle.ClipToWater"/>).
/// </para>
/// </remarks>
public sealed class SeaTemperatureStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.sea.temperature"</c>.</summary>
    public const string Key = "weather.sea.temperature";

    /// <summary>0 °C in kelvin.</summary>
    public const double CelsiusZero = 273.15;

    /// <summary>The band boundaries in degrees Celsius, lowest first: 0, 1, … 30.</summary>
    public static IReadOnlyList<double> BoundariesCelsius { get; } = Enumerable.Range(0, 31).Select(c => (double)c).ToArray();

    /// <summary>The bands, coldest first, in kelvin.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } = BuildBands();

    /// <summary>Creates the style, with the Day palette active.</summary>
    public SeaTemperatureStyle()
        : base(Key, "Sea surface temperature", WeatherLayerKind.Fill, WeatherParameters.SeaSurfaceTemperature, null,
            isVisibleByDefault: true, clipToWater: true)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);

    /// <summary>Converts a temperature in kelvin to degrees Celsius.</summary>
    /// <param name="kelvin">The temperature in kelvin.</param>
    public static double ToCelsius(double kelvin) => kelvin - CelsiusZero;

    private static WeatherBand[] BuildBands()
    {
        // The lowest band starts at -5 °C, under the freezing point of sea
        // water (about -1.9 °C), so every sea temperature has a band.
        var bands = new WeatherBand[BoundariesCelsius.Count + 1];
        for (int i = 0; i < bands.Length; i++)
        {
            double minimum = i == 0 ? -5 : BoundariesCelsius[i - 1];
            double maximum = i < BoundariesCelsius.Count ? BoundariesCelsius[i] : double.PositiveInfinity;
            string label = i == 0
                ? $"below {Format(maximum)} °C"
                : double.IsPositiveInfinity(maximum)
                    ? $"{Format(minimum)} °C and higher"
                    : $"{Format(minimum)}–{Format(maximum)} °C";
            float max = double.IsPositiveInfinity(maximum) ? float.PositiveInfinity : (float)(maximum + CelsiusZero);
            bands[i] = new WeatherBand((float)(minimum + CelsiusZero), max, WeatherPalette.SeaTemperatureToken(i + 1), label);
        }

        return bands;
    }

    private static string Format(double celsius) => celsius.ToString("0", CultureInfo.InvariantCulture);
}
