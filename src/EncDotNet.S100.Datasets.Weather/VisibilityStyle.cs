using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The forecast visibility (<c>visibility</c>) as a sparse colour fill in the
/// three restricted bands of the marine forecast scale; good visibility
/// (5 nautical miles and more) isn't drawn. On by default.
/// </summary>
/// <remarks>
/// <para>
/// The bands are the visibility terms of marine forecasts such as the
/// shipping forecast (<see cref="Bands"/>): very poor (under 1000 m, fog),
/// poor (1000 m to 2 nautical miles) and moderate (2 to 5 nautical miles).
/// Leaving good visibility out keeps the chart clear where there's nothing
/// to see: a band's colour marks only the areas a mariner plans around.
/// </para>
/// <para>
/// The field is stored in metres (<see cref="WeatherUnit.Metres"/>); the
/// bands are labelled in the forecast's own units. Colours are the grey ramp
/// of <see cref="WeatherPalette"/>, drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>. Visibility only matters
/// on the water, so the fill is clipped to water when an S-101 chart is
/// loaded (<see cref="WeatherCoverageStyle.ClipToWater"/>).
/// </para>
/// </remarks>
public sealed class VisibilityStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.visibility"</c>.</summary>
    public const string Key = "weather.visibility";

    /// <summary>The upper limit of very poor visibility (fog): 1000 m.</summary>
    public static Length VeryPoorLimit { get; } = Length.FromMetres(1000);

    /// <summary>The upper limit of poor visibility: 2 nautical miles.</summary>
    public static Length PoorLimit { get; } = Length.FromNauticalMiles(2);

    /// <summary>The upper limit of moderate visibility: 5 nautical miles. Anything longer is good, and isn't drawn.</summary>
    public static Length ModerateLimit { get; } = Length.FromNauticalMiles(5);

    /// <summary>The bands, shortest visibility first, in metres: very poor, poor and moderate.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } =
    [
        new(0, (float)VeryPoorLimit.TotalMetres, WeatherPalette.VisibilityToken(1), "Very poor (fog), under 1000 m"),
        new((float)VeryPoorLimit.TotalMetres, (float)PoorLimit.TotalMetres, WeatherPalette.VisibilityToken(2), "Poor, 1000 m to 2 nm"),
        new((float)PoorLimit.TotalMetres, (float)ModerateLimit.TotalMetres, WeatherPalette.VisibilityToken(3), "Moderate, 2 to 5 nm"),
    ];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public VisibilityStyle()
        : base(Key, "Visibility", WeatherLayerKind.Fill, WeatherParameters.Visibility, null, isVisibleByDefault: true, clipToWater: true)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);
}
