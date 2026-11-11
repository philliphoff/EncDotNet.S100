using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>The position legend's text (D17), shared by the Legend tab and <c>get_legend</c>.</summary>
internal static class PositionLegendText
{
    /// <summary>"Depths in metres · Mean Lower Low Water · Safety contour 10 m".</summary>
    /// <param name="legend">The legend.</param>
    /// <returns>The first line.</returns>
    public static string SettingsLine(PositionLegend legend)
    {
        ArgumentNullException.ThrowIfNull(legend);
        var parts = new List<string>(3) { Units(legend.DepthUnit) };
        if (legend.HasDatum)
            parts.Add(Datum(legend.DatumCode));
        parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.PositionLegend_SafetyContourFormat, Depth(legend.SafetyContourMetres, legend.DepthUnit)));
        return string.Join(Strings.PositionLegend_Separator, parts);
    }

    /// <summary>
    /// What follows the cell's name: "ed. 4 · compiled 1:12 000 · shown 1:22 000",
    /// with "· 1.8× overscale" when the display is finer than the compilation
    /// scale; "No chart at the view centre" with no cell.
    /// </summary>
    /// <param name="legend">The legend.</param>
    /// <returns>The second line, after the cell's name.</returns>
    public static string CellDetail(PositionLegend legend)
    {
        ArgumentNullException.ThrowIfNull(legend);
        if (legend.Cell is not { } cell)
            return Strings.PositionLegend_NoChart;

        var parts = new List<string>(4);
        if (cell.Edition is { } edition)
        {
            parts.Add(edition.Update > 0
                ? string.Format(CultureInfo.CurrentCulture, Strings.PositionLegend_EditionUpdateFormat, edition.Number, edition.Update)
                : string.Format(CultureInfo.CurrentCulture, Strings.PositionLegend_EditionFormat, edition.Number));
        }
        if (cell.CompilationScale is int compiled)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.PositionLegend_CompiledFormat, MapScaleFormatter.FormatDenominator(compiled)));
        if (legend.DisplayScale is double shown)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.PositionLegend_ShownFormat, MapScaleFormatter.FormatDenominator(shown)));
        if (legend.OverscaleFactor is double factor)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.PositionLegend_OverscaleFormat, factor));
        return string.Join(Strings.PositionLegend_Separator, parts);
    }

    /// <summary>The whole second line: the cell's name and <see cref="CellDetail"/>.</summary>
    /// <param name="legend">The legend.</param>
    /// <returns>"US5WA31M ed. 4 · compiled 1:12 000 · shown 1:22 000", or the no-chart text.</returns>
    public static string CellLine(PositionLegend legend)
    {
        ArgumentNullException.ThrowIfNull(legend);
        var detail = CellDetail(legend);
        return legend.Cell is { } cell
            ? detail.Length == 0 ? cell.Name : cell.Name + " " + detail
            : detail;
    }

    /// <summary>"Depths in metres".</summary>
    /// <param name="unit">The Mariner's depth unit.</param>
    /// <returns>The units item.</returns>
    public static string Units(DepthUnit unit) => unit switch
    {
        DepthUnit.Feet => Strings.PositionLegend_DepthsFeet,
        DepthUnit.FathomsFeet => Strings.PositionLegend_DepthsFathomsFeet,
        DepthUnit.Fathoms => Strings.PositionLegend_DepthsFathoms,
        _ => Strings.PositionLegend_DepthsMetres,
    };

    /// <summary>The datum's name, as the depth basis names it, or "Datum not declared".</summary>
    /// <param name="code">The S-100 vertical datum code.</param>
    /// <returns>The datum item.</returns>
    public static string Datum(int? code) =>
        code is null ? Strings.PositionLegend_DatumNotDeclared : DepthBasisText.DatumLabel(code);

    /// <summary>A depth in the Mariner's unit: whole values without decimals ("10 m", "30.5 m"), never split across lines.</summary>
    private static string Depth(double metres, DepthUnit unit)
    {
        var shown = DepthFormatting.ToDisplay(metres, unit);
        var whole = Math.Abs(shown - Math.Round(shown)) < 0.05;
        // A no-break space keeps the number with its unit when the row wraps.
        return DepthFormatting.Format(metres, unit, whole ? 0 : null).Replace(' ', '\u00A0');
    }
}
