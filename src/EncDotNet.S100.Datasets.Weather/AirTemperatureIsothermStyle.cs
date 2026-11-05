using System.Globalization;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The 2 m air temperature (<c>air.temperature</c>) as dashed isotherms every
/// <see cref="IntervalCelsius"/> °C, labelled in degrees Celsius (e.g.
/// <c>-5°</c>). Off by default.
/// </summary>
/// <remarks>
/// <para>
/// Lines rather than a fill: the sea surface temperature fill already colours
/// the sea, and a second temperature fill over it would hide it. The lines
/// are dashed so they don't read as the solid sea isotherms
/// (<see cref="SeaTemperatureIsothermStyle"/>). The 0 °C line is always one
/// of them: freezing spray (<see cref="FreezingSprayStyle"/>) starts just
/// below it.
/// </para>
/// <para>
/// The isotherms are value-based <see cref="ContourStyle"/>s in the field's
/// stored unit, kelvin, from <see cref="LowestCelsius"/> to
/// <see cref="HighestCelsius"/> °C, on S-98
/// <see cref="S98DisplayPlane.OtherChartOverlays"/> at priority
/// <see cref="WeatherCoverageStyle.IsolinePriority"/>. Air temperature is
/// drawn over land too: isolines have no clip.
/// </para>
/// </remarks>
public sealed class AirTemperatureIsothermStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.air.temperature.isotherms"</c>.</summary>
    public const string Key = "weather.air.temperature.isotherms";

    /// <summary>The isotherm interval: 5 °C.</summary>
    public const int IntervalCelsius = 5;

    /// <summary>The lowest isotherm: −50 °C.</summary>
    public const int LowestCelsius = -50;

    /// <summary>The highest isotherm: 50 °C.</summary>
    public const int HighestCelsius = 50;

    /// <summary>The line width, in device-independent pixels.</summary>
    public const float LineWidth = 1.0f;

    /// <summary>The dash pattern: 6 on, 3 off, in device-independent pixels.</summary>
    public static IReadOnlyList<float> DashPattern { get; } = [6f, 3f];

    /// <summary>The isotherm levels in kelvin, lowest first.</summary>
    public static IReadOnlyList<double> LevelsKelvin { get; } = Enumerable
        .Range(0, (HighestCelsius - LowestCelsius) / IntervalCelsius + 1)
        .Select(i => LowestCelsius + i * IntervalCelsius + SeaTemperatureStyle.CelsiusZero)
        .ToArray();

    private readonly Dictionary<PaletteType, ContourStyle[]> _contours = [];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public AirTemperatureIsothermStyle()
        : base(Key, "Air temperature", WeatherLayerKind.Isolines, WeatherParameters.AirTemperature, null, isVisibleByDefault: false)
    {
    }

    /// <summary>
    /// One dashed, labelled contour per <see cref="LevelsKelvin"/> level, in
    /// kelvin, in the active palette's <see cref="WeatherPalette.AirIsothermInkToken"/>.
    /// </summary>
    public override IReadOnlyList<ContourStyle> Contours
    {
        get
        {
            var palette = ActivePaletteType;
            if (!_contours.TryGetValue(palette, out var contours))
            {
                string ink = ActivePalette.Resolve(WeatherPalette.AirIsothermInkToken);
                contours = LevelsKelvin
                    .Select(level => new ContourStyle
                    {
                        Value = level,
                        Unit = "K",
                        LineWidth = LineWidth,
                        Color = ink,
                        DashPattern = [.. DashPattern],
                        ShowLabel = true,
                    })
                    .ToArray();
                _contours[palette] = contours;
            }

            return contours;
        }
    }

    /// <summary>The label of an isotherm: its temperature in whole degrees Celsius, e.g. <c>"-5°"</c>.</summary>
    /// <param name="contour">An isotherm, valued in kelvin.</param>
    public override string FormatContourLabel(ContourStyle contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        return Math.Round(SeaTemperatureStyle.ToCelsius(contour.Value)).ToString("0", CultureInfo.InvariantCulture) + "°";
    }
}
