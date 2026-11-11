using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Builds legend rows (<see cref="LegendEntry"/>) from a portrayal
/// catalogue's own assets: its colour tokens, symbols and line styles,
/// resolved against the palette the sub-layer is drawn in. The counterpart of
/// <c>LegendBuilder</c> for products portrayed by a catalogue rather than in code.
/// </summary>
/// <remarks>
/// Build the rows with the palette the sub-layer is drawn in and build them
/// again when the palette changes, so the legend shows the colours on the map.
/// </remarks>
public sealed class CatalogueLegendBuilder
{
    private readonly ColorPalette _palette;
    private readonly Func<string, string?>? _symbols;
    private readonly Func<string, LineStyle?>? _lineStyles;

    /// <summary>Creates a legend builder.</summary>
    /// <param name="palette">The palette the sub-layer is drawn in.</param>
    /// <param name="symbols">Resolves a catalogue symbol name to its SVG, palette classes unresolved; <see langword="null"/> when the legend has no symbols.</param>
    /// <param name="lineStyles">Resolves a catalogue line style name; <see langword="null"/> when the legend has no named line styles.</param>
    public CatalogueLegendBuilder(ColorPalette palette, Func<string, string?>? symbols = null, Func<string, LineStyle?>? lineStyles = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _symbols = symbols;
        _lineStyles = lineStyles;
    }

    /// <summary>A colour swatch row.</summary>
    /// <param name="color">A colour token, or a <c>#RRGGBB</c> colour.</param>
    /// <param name="label">The row's label.</param>
    /// <param name="transparency">The fill's transparency, 0 (opaque) to 1, or <see langword="null"/> for opaque.</param>
    public SwatchLegendEntry Swatch(string color, string label, double? transparency = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new SwatchLegendEntry(Color(color, transparency), label);
    }

    /// <summary>An unfilled area row: a transparent swatch inside the area's outline.</summary>
    /// <param name="label">The row's label.</param>
    /// <param name="outline">The outline.</param>
    public static SwatchLegendEntry Area(string label, LegendLineSample outline)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(outline);
        return new SwatchLegendEntry("#00000000", label) { Outline = outline };
    }

    /// <summary>A point symbol row.</summary>
    /// <param name="symbolName">The catalogue symbol's name.</param>
    /// <param name="label">The row's label.</param>
    /// <exception cref="InvalidOperationException">The builder has no symbol resolver.</exception>
    /// <exception cref="KeyNotFoundException">The catalogue has no such symbol.</exception>
    public SymbolLegendEntry Symbol(string symbolName, string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new SymbolLegendEntry(SvgProcessor.Process(RawSymbol(symbolName), _palette), label);
    }

    /// <summary>A line row drawn with a catalogue line style.</summary>
    /// <param name="lineStyleName">The catalogue line style's name.</param>
    /// <param name="label">The row's label.</param>
    /// <exception cref="InvalidOperationException">The builder has no line style resolver.</exception>
    /// <exception cref="KeyNotFoundException">The catalogue has no such line style.</exception>
    public LineLegendEntry Line(string lineStyleName, string label)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineStyleName);
        ArgumentNullException.ThrowIfNull(label);
        if (_lineStyles is null)
            throw new InvalidOperationException($"The legend has no line style resolver to draw '{lineStyleName}' from.");
        var style = _lineStyles(lineStyleName)
            ?? throw new KeyNotFoundException($"The catalogue has no line style '{lineStyleName}'.");
        return new LineLegendEntry(Sample(style), label);
    }

    /// <summary>A sample of a catalogue line style, resolved for the builder's palette.</summary>
    /// <param name="style">The line style; for a composite style, its first component is sampled.</param>
    public LegendLineSample Sample(LineStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (style.Components.Count > 0)
            style = style.Components[0];

        var dashes = new List<(double Start, double Length)>();
        if (style.DashPattern is { } pattern)
        {
            for (int i = 0; i + 1 < pattern.Length; i += 2)
                dashes.Add((pattern[i], pattern[i + 1]));
        }

        var symbols = new List<LegendLineSymbol>(style.Symbols.Count);
        foreach (var symbol in style.Symbols)
        {
            var raw = RawSymbol(symbol.Reference);
            var pivot = SvgPivotMetrics.TryParse(raw)?.RelativeOffset ?? (0.0, 0.0);
            symbols.Add(new LegendLineSymbol(SvgProcessor.Process(raw, _palette), symbol.Position, symbol.Rotation, pivot.X, pivot.Y));
        }

        return new LegendLineSample
        {
            Color = Color(style.Color, null),
            Width = style.HasPen ? style.Width : 0,
            Dashes = dashes,
            IntervalLength = style.IntervalLength,
            Symbols = symbols,
        };
    }

    /// <summary>
    /// A sample of a simple line drawn inline by a line instruction (an inline
    /// <c>lineStyle</c>, or <see cref="LineInstruction.SimpleLineStyleReference"/>):
    /// its colour, width and dashes.
    /// </summary>
    /// <param name="line">The line instruction.</param>
    public LegendLineSample Sample(LineInstruction line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var dashes = line.Dashes is { } d ? d.Select(x => (x.Offset, x.Length)).ToArray() : [];
        double interval = line.DashIntervalLength;
        if (interval <= 0 && dashes.Length > 0)
            interval = dashes.Sum(x => x.Length) * 2; // each dash followed by a gap as long as itself
        return new LegendLineSample
        {
            Color = Color(line.LineColor ?? "CHBLK", null),
            Width = line.LineWidth,
            Dashes = dashes,
            IntervalLength = interval,
        };
    }

    /// <summary>
    /// The legend of a symbol scheme: one symbol row per band, lowest first,
    /// with the band's label, or empty when any band has no
    /// <see cref="SymbolBand.Label"/>.
    /// </summary>
    /// <param name="scheme">The symbol scheme, e.g. the S-111 speed bands.</param>
    public IReadOnlyList<LegendEntry> SymbolBands(CoverageSymbolScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var entries = new LegendEntry[scheme.Bands.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var band = scheme.Bands[i];
            if (string.IsNullOrWhiteSpace(band.Label))
                return [];
            entries[i] = Symbol(band.SymbolRef, band.Label);
        }

        return entries;
    }

    private string Color(string color, double? transparency)
    {
        ArgumentException.ThrowIfNullOrEmpty(color);
        if (transparency is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(transparency), transparency, "Transparency must be between 0 and 1.");

        var hex = color.StartsWith('#') ? color : _palette.Resolve(color);
        if (transparency is not { } t || t == 0)
            return hex;

        var rgb = RgbaColor.FromHex(hex);
        byte alpha = (byte)Math.Round(255 * (1 - t));
        return string.Create(CultureInfo.InvariantCulture, $"#{alpha:X2}{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}");
    }

    private string RawSymbol(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_symbols is null)
            throw new InvalidOperationException($"The legend has no symbol resolver to draw '{name}' from.");
        return _symbols(name)
            ?? throw new KeyNotFoundException($"The catalogue has no symbol '{name}'.");
    }
}
