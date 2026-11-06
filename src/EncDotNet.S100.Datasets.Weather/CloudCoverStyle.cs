using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The total cloud cover (<see cref="WeatherParameters.CloudCover"/>) as a
/// grey veil in three bands from 25 %, drawn at
/// <see cref="WeatherCoverageStyle.VeilOpacity"/>; clearer skies aren't
/// drawn. Off by default.
/// </summary>
/// <remarks>
/// <para>
/// The bands (<see cref="Bands"/>) are partly cloudy (25 to 50 %), mostly
/// cloudy (50 to 75 %) and cloudy or overcast (75 % and more). Cloud covers
/// wide areas, so the veil is lighter than the other fills, and the chart
/// reads through it; it's clipped to water when an S-101 chart is loaded
/// (<see cref="WeatherCoverageStyle.ClipToWater"/>). Colours are the grey
/// ramp of <see cref="WeatherPalette"/>.
/// </para>
/// </remarks>
public sealed class CloudCoverStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.cloud"</c>.</summary>
    public const string Key = "weather.cloud";

    /// <summary>The bands, clearest first, in percent.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } =
    [
        new(25, 50, WeatherPalette.CloudToken(1), "Partly cloudy, 25 to 50 %"),
        new(50, 75, WeatherPalette.CloudToken(2), "Mostly cloudy, 50 to 75 %"),
        new(75, float.PositiveInfinity, WeatherPalette.CloudToken(3), "Cloudy, 75 % and more"),
    ];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public CloudCoverStyle()
        : base(Key, "Cloud cover", WeatherLayerKind.Fill, WeatherParameters.CloudCover, null,
            isVisibleByDefault: false, clipToWater: true, opacity: VeilOpacity)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);
}
