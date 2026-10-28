using System.Globalization;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Mean sea level pressure (<c>pressure.msl</c>) as isobars every
/// <see cref="IntervalHectopascals"/> hPa, labelled in hectopascals (e.g.
/// <c>1012</c>). Off by default.
/// </summary>
/// <remarks>
/// <para>
/// The isobars are value-based <see cref="ContourStyle"/>s
/// (<see cref="Contours"/>) in the field's stored unit, pascals, from
/// <see cref="LowestHectopascals"/> to <see cref="HighestHectopascals"/>
/// hPa, beyond the recorded extremes; only the levels a grid's values reach
/// are traced. <see cref="WeatherPortrayal"/> traces them with marching
/// squares through the sampled grid
/// (<see cref="Pipelines.Portrayal.CoverageContours"/>) into an
/// <see cref="Pipelines.Portrayal.IsolineCoverageSubLayer"/>, which both
/// renderers draw as solid lines of constant pixel width with labels along
/// them.
/// </para>
/// <para>
/// Layer order (design note weather-sources.md §5): S-98
/// <see cref="S98DisplayPlane.OtherChartOverlays"/> at
/// priority <see cref="WeatherCoverageStyle.IsolinePriority"/> (90), under
/// the observations. Pressure is drawn over land as well as sea, so the lines
/// aren't clipped to water.
/// </para>
/// </remarks>
public sealed class PressureIsobarStyle : WeatherCoverageStyle
{
    /// <summary>The sub-layer key, <c>"weather.pressure.isobars"</c>.</summary>
    public const string Key = "weather.pressure.isobars";

    /// <summary>The isobar interval: 4 hPa.</summary>
    public const int IntervalHectopascals = 4;

    /// <summary>The lowest isobar: 860 hPa, under any recorded sea level pressure.</summary>
    public const int LowestHectopascals = 860;

    /// <summary>The highest isobar: 1100 hPa, over any recorded sea level pressure.</summary>
    public const int HighestHectopascals = 1100;

    /// <summary>The line width, in device-independent pixels.</summary>
    public const float LineWidth = 1.2f;

    /// <summary>The isobar levels in pascals, lowest first: 86000, 86400, … 110000.</summary>
    public static IReadOnlyList<double> LevelsPascals { get; } = Enumerable
        .Range(0, (HighestHectopascals - LowestHectopascals) / IntervalHectopascals + 1)
        .Select(i => (LowestHectopascals + i * IntervalHectopascals) * 100.0)
        .ToArray();

    private readonly Dictionary<PaletteType, ContourStyle[]> _contours = [];

    /// <summary>Creates the style, with the Day palette active.</summary>
    public PressureIsobarStyle()
        : base(Key, "Isobars", WeatherLayerKind.Isolines, WeatherParameters.PressureMsl, null, isVisibleByDefault: false)
    {
    }

    /// <summary>
    /// One solid, labelled contour per <see cref="LevelsPascals"/> level, in
    /// pascals, in the active palette's <see cref="WeatherPalette.IsobarInkToken"/>.
    /// </summary>
    public override IReadOnlyList<ContourStyle> Contours
    {
        get
        {
            var palette = ActivePaletteType;
            if (!_contours.TryGetValue(palette, out var contours))
            {
                string ink = ActivePalette.Resolve(WeatherPalette.IsobarInkToken);
                contours = LevelsPascals
                    .Select(level => new ContourStyle
                    {
                        Value = level,
                        Unit = "Pa",
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

    /// <summary>The label of an isobar: its pressure in whole hectopascals, e.g. <c>"1012"</c>.</summary>
    /// <param name="contour">An isobar, valued in pascals.</param>
    public override string FormatContourLabel(ContourStyle contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        return Math.Round(contour.Value / 100.0).ToString("0", CultureInfo.InvariantCulture);
    }
}
