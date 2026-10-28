using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The significant wave height (<c>wave.hs</c>) as a colour fill in 15 bands,
/// the Douglas sea-state boundaries made finer. On by default.
/// </summary>
/// <remarks>
/// The bands break at 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 12 and 14 m
/// (<see cref="Boundaries"/>); the top band is 14 m and higher. Colours are
/// the violet ramp of <see cref="WeatherPalette"/>, drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>. Waves are a sea quantity,
/// so the fill is clipped to water when an S-101 chart is loaded
/// (<see cref="WeatherCoverageStyle.ClipToWater"/>).
/// </remarks>
public sealed class WaveHeightStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.wave.height"</c>.</summary>
    public const string Key = "weather.wave.height";

    /// <summary>The band boundaries in metres, lowest first.</summary>
    public static IReadOnlyList<double> Boundaries { get; } = [0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 12, 14];

    /// <summary>The bands, lowest first, in metres.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } = BuildBands();

    /// <summary>Creates the style, with the Day palette active.</summary>
    public WaveHeightStyle()
        : base(Key, "Wave height", WeatherLayerKind.Fill, WeatherParameters.WaveHeight, null, isVisibleByDefault: true, clipToWater: true)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);

    private static WeatherBand[] BuildBands()
    {
        var bands = new WeatherBand[Boundaries.Count + 1];
        for (int i = 0; i < bands.Length; i++)
        {
            double minimum = i == 0 ? 0 : Boundaries[i - 1];
            double maximum = i < Boundaries.Count ? Boundaries[i] : double.PositiveInfinity;
            string label = double.IsPositiveInfinity(maximum)
                ? $"{Format(minimum)} m and higher"
                : $"{Format(minimum)}–{Format(maximum)} m";
            bands[i] = new WeatherBand((float)minimum, (float)maximum, WeatherPalette.WaveHeightToken(i + 1), label);
        }

        return bands;
    }

    private static string Format(double metres) => metres.ToString("0.#", CultureInfo.InvariantCulture);
}
