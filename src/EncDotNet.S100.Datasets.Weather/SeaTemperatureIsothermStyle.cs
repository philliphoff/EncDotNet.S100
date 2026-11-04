using System.Globalization;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The sea surface temperature (<c>sea.temperature</c>) as isotherms every
/// <see cref="IntervalCelsius"/> °C, labelled in degrees Celsius (e.g.
/// <c>18°</c>). Off by default: the fill already shows the temperature, and
/// the lines are for reading a front's exact position.
/// </summary>
/// <remarks>
/// <para>
/// The isotherms are value-based <see cref="ContourStyle"/>s
/// (<see cref="Contours"/>) in the field's stored unit, kelvin, from
/// <see cref="LowestCelsius"/> to <see cref="HighestCelsius"/> °C; only the
/// levels a grid's values reach are traced, as for
/// <see cref="PressureIsobarStyle"/>. They're drawn on S-98
/// <see cref="S98DisplayPlane.OtherChartOverlays"/> at priority
/// <see cref="WeatherCoverageStyle.IsolinePriority"/>.
/// </para>
/// <para>
/// The lines aren't clipped to water: isolines have no clip, and SST grids
/// mark land as missing, so no line is traced over it.
/// </para>
/// </remarks>
public sealed class SeaTemperatureIsothermStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.sea.temperature.isotherms"</c>.</summary>
    public const string Key = "weather.sea.temperature.isotherms";

    /// <summary>The isotherm interval: 2 °C.</summary>
    public const int IntervalCelsius = 2;

    /// <summary>The lowest isotherm: -2 °C, about the freezing point of sea water.</summary>
    public const int LowestCelsius = -2;

    /// <summary>The highest isotherm: 36 °C, over any open-ocean temperature.</summary>
    public const int HighestCelsius = 36;

    /// <summary>The line width, in device-independent pixels.</summary>
    public const float LineWidth = 1.0f;

    /// <summary>The isotherm levels in kelvin, lowest first.</summary>
    public static IReadOnlyList<double> LevelsKelvin { get; } = Enumerable
        .Range(0, (HighestCelsius - LowestCelsius) / IntervalCelsius + 1)
        .Select(i => LowestCelsius + i * IntervalCelsius + SeaTemperatureStyle.CelsiusZero)
        .ToArray();

    private readonly Dictionary<PaletteType, ContourStyle[]> _contours = [];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public SeaTemperatureIsothermStyle()
        : base(Key, "Isotherms", WeatherLayerKind.Isolines, WeatherParameters.SeaSurfaceTemperature, null, isVisibleByDefault: false)
    {
    }

    /// <summary>
    /// One solid, labelled contour per <see cref="LevelsKelvin"/> level, in
    /// kelvin, in the active palette's <see cref="WeatherPalette.IsothermInkToken"/>.
    /// </summary>
    public override IReadOnlyList<ContourStyle> Contours
    {
        get
        {
            var palette = ActivePaletteType;
            if (!_contours.TryGetValue(palette, out var contours))
            {
                string ink = ActivePalette.Resolve(WeatherPalette.IsothermInkToken);
                contours = LevelsKelvin
                    .Select(level => new ContourStyle
                    {
                        Value = level,
                        Unit = "K",
                        LineWidth = LineWidth,
                        Color = ink,
                        ShowLabel = true,
                    })
                    .ToArray();
                _contours[palette] = contours;
            }

            return contours;
        }
    }

    /// <summary>The label of an isotherm: its temperature in whole degrees Celsius, e.g. <c>"18°"</c>.</summary>
    /// <param name="contour">An isotherm, valued in kelvin.</param>
    public override string FormatContourLabel(ContourStyle contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        return Math.Round(SeaTemperatureStyle.ToCelsius(contour.Value)).ToString("0", CultureInfo.InvariantCulture) + "°";
    }
}
