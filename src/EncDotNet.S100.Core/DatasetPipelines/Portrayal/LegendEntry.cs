using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// One row of a sub-layer's legend: a glyph and its label. A viewer shows a
/// sub-layer's legend (<see cref="GridCoverageSubLayer.Legend"/>,
/// <see cref="VectorSubLayer.Legend"/>) under its layer stack entry; the
/// renderers don't draw legends.
/// </summary>
/// <remarks>
/// <para>
/// A row is one of three kinds:
/// </para>
/// <list type="bullet">
///   <item><see cref="SwatchLegendEntry"/>: a colour swatch, optionally outlined (a colour band, an area).</item>
///   <item><see cref="SymbolLegendEntry"/>: a point symbol (a cyclone class symbol).</item>
///   <item><see cref="LineLegendEntry"/>: a sample of a line style, with any symbols along it (a front).</item>
/// </list>
/// <para>
/// Every colour and symbol in a row is already resolved against the palette
/// the sub-layer was drawn in, so a viewer draws the row as it is and gets a
/// new legend when the palette changes. The portrayal toolkit's
/// <c>LegendBuilder</c> builds rows from the same tokens, symbols and line
/// styles the portrayal draws with.
/// </para>
/// </remarks>
/// <param name="Label">The row's label, e.g. <c>"Cold front"</c>; also its accessible name.</param>
public abstract record LegendEntry(string Label)
{
    /// <summary>
    /// The legend of a colour scheme: one swatch per band, lowest first, or
    /// empty when any band has no <see cref="ColorBand.Label"/> (a legend with
    /// gaps would mislead).
    /// </summary>
    /// <param name="scheme">The colour scheme, with colours already resolved.</param>
    public static IReadOnlyList<LegendEntry> FromScheme(CoverageColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var entries = new LegendEntry[scheme.Bands.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var band = scheme.Bands[i];
            if (string.IsNullOrWhiteSpace(band.Label))
                return [];
            entries[i] = new SwatchLegendEntry(band.Color, band.Label);
        }

        return entries;
    }
}

/// <summary>
/// A legend row with a colour swatch: a band of a colour fill, e.g.
/// <c>#BDB2E3</c> and <c>"2–2.5 m"</c>, or an area with its outline, e.g. a
/// cyclone's 34 kt wind area.
/// </summary>
/// <param name="Color">The fill colour as a hex string (<c>#RRGGBB</c> or <c>#AARRGGBB</c>).</param>
/// <param name="Label">The row's label, e.g. <c>"Force 3, 7–10 kn"</c>.</param>
public sealed record SwatchLegendEntry(string Color, string Label) : LegendEntry(Label)
{
    /// <summary>
    /// The area's outline, drawn round the swatch, or <see langword="null"/>
    /// (the default) for a plain band swatch.
    /// </summary>
    public LegendLineSample? Outline { get; init; }
}

/// <summary>A legend row with a point symbol, e.g. a hurricane symbol and <c>"Hurricane, typhoon"</c>.</summary>
/// <param name="Svg">
/// The symbol's SVG with its palette classes already resolved to colours, as
/// <c>SvgProcessor.Process</c> leaves it. The viewer fits it to the glyph box.
/// </param>
/// <param name="Label">The row's label.</param>
public sealed record SymbolLegendEntry(string Svg, string Label) : LegendEntry(Label);

/// <summary>A legend row with a short sample of a line style, e.g. a cold front's line and pips.</summary>
/// <param name="Line">The line sample.</param>
/// <param name="Label">The row's label.</param>
public sealed record LineLegendEntry(LegendLineSample Line, string Label) : LegendEntry(Label);

/// <summary>
/// A line style resolved for a legend: pen, dashes and the symbols repeated
/// along it, all in millimetres on the S-100 display surface, with colours and
/// symbols resolved against one palette.
/// </summary>
public sealed record LegendLineSample
{
    /// <summary>The pen colour as a hex string.</summary>
    public required string Color { get; init; }

    /// <summary>The pen width in millimetres; 0 for a line drawn by its symbols only.</summary>
    public double Width { get; init; }

    /// <summary>
    /// The dashes as (start, length) pairs in millimetres within each
    /// <see cref="IntervalLength"/>, or empty for a solid line.
    /// </summary>
    public IReadOnlyList<(double Start, double Length)> Dashes { get; init; } = [];

    /// <summary>
    /// The length in millimetres of the pattern the dashes and symbols repeat
    /// over; 0 for a plain solid line.
    /// </summary>
    public double IntervalLength { get; init; }

    /// <summary>The symbols placed in each interval.</summary>
    public IReadOnlyList<LegendLineSymbol> Symbols { get; init; } = [];
}

/// <summary>A symbol a <see cref="LegendLineSample"/> places along its line.</summary>
/// <param name="Svg">The symbol's SVG, palette classes resolved.</param>
/// <param name="Position">Its distance from the start of each interval, in millimetres.</param>
/// <param name="Rotation">Its clockwise rotation relative to the line, in degrees; 180 puts it on the other side.</param>
/// <param name="PivotRelativeX">
/// The pivot's offset from the symbol's centre as a fraction of its width (+x:
/// pivot left of centre), as a renderer's resolved symbol carries it.
/// </param>
/// <param name="PivotRelativeY">
/// The pivot's offset from the symbol's centre as a fraction of its height (+y:
/// pivot above centre).
/// </param>
public sealed record LegendLineSymbol(
    string Svg,
    double Position,
    double Rotation,
    double PivotRelativeX,
    double PivotRelativeY);
