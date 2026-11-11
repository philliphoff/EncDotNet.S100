using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The freezing spray (vessel icing) risk
/// (<see cref="WeatherParameters.FreezingSprayPredictor"/>) as a sparse
/// colour fill in Overland's four icing classes: light, moderate, heavy and
/// extreme. Where there's no icing nothing is drawn. On by default.
/// </summary>
/// <remarks>
/// <para>
/// The bands are the classes of <see cref="FreezingSpray"/>, keyed on its
/// predictor, which the combined source derives from the wind, the 2 m air
/// temperature and the sea temperature
/// (<see cref="WeatherCoverageSource.CreateFreezingSpray"/>). Colours are
/// the plum ramp of <see cref="WeatherPalette"/>, drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>. Icing only happens at sea,
/// so the fill is clipped to water when an S-101 chart is loaded
/// (<see cref="WeatherCoverageStyle.ClipToWater"/>).
/// </para>
/// </remarks>
public sealed class FreezingSprayStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.spray"</c>.</summary>
    public const string Key = "weather.spray";

    /// <summary>The bands, lightest icing first, on the predictor.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } =
    [
        new(float.Epsilon, (float)FreezingSpray.LightLimit, WeatherPalette.FreezingSprayToken(1), "Light"),
        new((float)FreezingSpray.LightLimit, (float)FreezingSpray.ModerateLimit, WeatherPalette.FreezingSprayToken(2), "Moderate"),
        new((float)FreezingSpray.ModerateLimit, (float)FreezingSpray.HeavyLimit, WeatherPalette.FreezingSprayToken(3), "Heavy"),
        new((float)FreezingSpray.HeavyLimit, float.PositiveInfinity, WeatherPalette.FreezingSprayToken(4), "Extreme"),
    ];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public FreezingSprayStyle()
        : base(Key, "Freezing spray", WeatherLayerKind.Fill, WeatherParameters.FreezingSprayPredictor, null,
            isVisibleByDefault: true, clipToWater: true)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);
}
