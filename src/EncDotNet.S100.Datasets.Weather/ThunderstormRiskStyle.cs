using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The thunderstorm risk, from the convective available potential energy
/// (<see cref="WeatherParameters.Cape"/>), as a sparse colour fill in three
/// classes from <see cref="RiskThreshold"/> (1000 J/kg); a more stable
/// atmosphere isn't drawn. Off by default.
/// </summary>
/// <remarks>
/// <para>
/// CAPE is the energy a parcel of air rising from the surface can gain, and
/// so how strong an updraught, and a thunderstorm, can become once something
/// sets it off. It's a risk, not a forecast of storms: it says nothing about
/// whether the air will be lifted. The classes (<see cref="Bands"/>) are the
/// usual forecasting ones: moderate instability from 1000 J/kg, high from
/// 2500 J/kg and extreme from 4000 J/kg. Weak instability, under 1000 J/kg,
/// is common over warm seas and seldom gives squalls worth planning around,
/// so it isn't drawn and the chart stays clear.
/// </para>
/// <para>
/// Colours are the ochre ramp of <see cref="WeatherPalette"/>, drawn at
/// <see cref="WeatherCoverageStyle.FillOpacity"/>, clipped to water when an
/// S-101 chart is loaded (<see cref="WeatherCoverageStyle.ClipToWater"/>).
/// </para>
/// </remarks>
public sealed class ThunderstormRiskStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.cape"</c>.</summary>
    public const string Key = "weather.cape";

    /// <summary>The CAPE under which nothing is drawn: 1000 J/kg.</summary>
    public const double RiskThreshold = 1000;

    /// <summary>The CAPE from which the risk is high: 2500 J/kg.</summary>
    public const double HighThreshold = 2500;

    /// <summary>The CAPE from which the risk is extreme: 4000 J/kg.</summary>
    public const double ExtremeThreshold = 4000;

    /// <summary>The classes, lowest first, in J/kg.</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } =
    [
        new((float)RiskThreshold, (float)HighThreshold, WeatherPalette.CapeToken(1), "Moderate, 1000 to 2500 J/kg"),
        new((float)HighThreshold, (float)ExtremeThreshold, WeatherPalette.CapeToken(2), "High, 2500 to 4000 J/kg"),
        new((float)ExtremeThreshold, float.PositiveInfinity, WeatherPalette.CapeToken(3), "Extreme, 4000 J/kg and more"),
    ];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public ThunderstormRiskStyle()
        : base(Key, "Thunderstorm risk", WeatherLayerKind.Fill, WeatherParameters.Cape, null,
            isVisibleByDefault: false, clipToWater: true)
    {
    }

    /// <summary>
    /// The risk class of a CAPE value: <c>"moderate"</c>, <c>"high"</c> or
    /// <c>"extreme"</c>, or <see langword="null"/> under <see cref="RiskThreshold"/>.
    /// </summary>
    /// <param name="joulesPerKilogram">The CAPE, in J/kg.</param>
    public static string? Classify(double joulesPerKilogram) => joulesPerKilogram switch
    {
        >= ExtremeThreshold => "extreme",
        >= HighThreshold => "high",
        >= RiskThreshold => "moderate",
        _ => null,
    };

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);
}
