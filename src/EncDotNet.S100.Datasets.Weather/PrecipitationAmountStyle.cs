using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The accumulated precipitation (<see cref="WeatherParameters.PrecipitationAmount"/>)
/// as a colour fill on the rain radar ramp of <see cref="PrecipitationStyle"/>,
/// from 0.2 mm to 50 mm and more over the step's period; where less fell
/// nothing is drawn. Off by default.
/// </summary>
/// <remarks>
/// <para>
/// The bands (<see cref="Bands"/>) are amounts, not rates: the period each
/// step covers is stated with its pick (<see cref="WeatherTimeStep.Interval"/>,
/// usually the 3 hours since the step before for GFS). They're labelled
/// without intensity words, which belong to a rate. The field is stored in
/// kg m⁻², which is millimetres of water
/// (<see cref="WeatherUnit.KilogramsPerSquareMetre"/>).
/// </para>
/// <para>
/// Drawn at <see cref="WeatherCoverageStyle.FillOpacity"/> and clipped to
/// water when an S-101 chart is loaded
/// (<see cref="WeatherCoverageStyle.ClipToWater"/>), as the rate is.
/// </para>
/// </remarks>
public sealed class PrecipitationAmountStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.precipitation.amount"</c>.</summary>
    public const string Key = "weather.precipitation.amount";

    /// <summary>The amount under which nothing is drawn: 0.2 mm.</summary>
    public const double LeastMillimetres = 0.2;

    /// <summary>The bands, least first, in kg m⁻² (millimetres).</summary>
    public static IReadOnlyList<WeatherBand> Bands { get; } =
    [
        Band(LeastMillimetres, 1, 1, "0.2 to 1 mm"),
        Band(1, 2.5, 2, "1 to 2.5 mm"),
        Band(2.5, 10, 3, "2.5 to 10 mm"),
        Band(10, 25, 4, "10 to 25 mm"),
        Band(25, 50, 5, "25 to 50 mm"),
        Band(50, double.PositiveInfinity, 6, "50 mm and more"),
    ];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public PrecipitationAmountStyle()
        : base(Key, "Precipitation amount", WeatherLayerKind.Fill, WeatherParameters.PrecipitationAmount, null,
            isVisibleByDefault: false, clipToWater: true)
    {
    }

    /// <inheritdoc/>
    public override CoverageColorScheme ResolveColorScheme(MarinerSettings settings) => BuildColorScheme(Bands);

    private static WeatherBand Band(double fromMillimetres, double toMillimetres, int band, string label) =>
        new((float)fromMillimetres, (float)toMillimetres, WeatherPalette.PrecipitationToken(band), label);
}
