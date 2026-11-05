using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// Builds the legend rows of a portrayal defined in code
/// (<see cref="LegendEntry"/>), from the same colour tokens, symbols and line
/// styles it draws with, resolved for one palette.
/// </summary>
/// <remarks>
/// Build the legend with the palette the sub-layer is drawn in, and build it
/// again when the palette changes, so the Layer stack shows the colours on the
/// map:
/// <code>
/// var legend = new LegendBuilder(WeatherPalette.Palette, context.Palette, Symbols.Provider);
/// subLayer.Legend =
/// [
///     legend.Symbol("WXTC_HURRICANE", "Hurricane, typhoon"),
///     legend.Line(ColdFront, "Cold front"),
///     legend.Area("WXTC_R34", "34 kt winds", legend.Sample("WXTCINK", 0.3, 0.6, 0.6)),
/// ];
/// </code>
/// </remarks>
public sealed class LegendBuilder
{
    private readonly ApplicationPalette _palette;
    private readonly PaletteType _paletteType;
    private readonly ColorPalette _colors;
    private readonly Func<string, string?>? _symbols;

    /// <summary>Creates a legend builder.</summary>
    /// <param name="palette">The portrayal's palette.</param>
    /// <param name="paletteType">The palette the sub-layer is drawn in.</param>
    /// <param name="symbols">
    /// Resolves a symbol name to its SVG, as the portrayal's
    /// <see cref="SymbolSet.Provider"/> does; <see langword="null"/> when the
    /// legend has no symbols.
    /// </param>
    public LegendBuilder(ApplicationPalette palette, PaletteType paletteType, Func<string, string?>? symbols = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _paletteType = paletteType;
        _colors = palette.GetColorPalette(paletteType);
        _symbols = symbols;
    }

    /// <summary>A colour swatch row, e.g. a band of a fill.</summary>
    /// <param name="colorToken">The fill's colour token.</param>
    /// <param name="label">The row's label.</param>
    /// <param name="transparency">The fill's transparency, 0 (opaque) to 1, or <see langword="null"/> for opaque.</param>
    /// <exception cref="KeyNotFoundException">The palette has no such token.</exception>
    public SwatchLegendEntry Swatch(string colorToken, string label, double? transparency = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new SwatchLegendEntry(Color(colorToken, transparency), label);
    }

    /// <summary>An area row: a swatch of the fill inside the area's outline, e.g. a cyclone's cone.</summary>
    /// <param name="fillToken">The fill's colour token.</param>
    /// <param name="label">The row's label.</param>
    /// <param name="outline">The outline, from <see cref="Sample(ComplexLineStyle)"/> or <see cref="Sample(string, double, double, double)"/>.</param>
    /// <param name="transparency">The fill's transparency, 0 (opaque) to 1, or <see langword="null"/> for opaque.</param>
    /// <exception cref="KeyNotFoundException">The palette has no such token.</exception>
    public SwatchLegendEntry Area(string fillToken, string label, LegendLineSample outline, double? transparency = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(outline);
        return new SwatchLegendEntry(Color(fillToken, transparency), label) { Outline = outline };
    }

    /// <summary>A point symbol row.</summary>
    /// <param name="symbolName">The symbol's name, resolved by the builder's symbol provider.</param>
    /// <param name="label">The row's label.</param>
    /// <exception cref="InvalidOperationException">The builder has no symbol provider.</exception>
    /// <exception cref="KeyNotFoundException">The provider has no such symbol.</exception>
    public SymbolLegendEntry Symbol(string symbolName, string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new SymbolLegendEntry(ResolveSymbol(symbolName), label);
    }

    /// <summary>A line row: a sample of a complex line style with its symbols.</summary>
    /// <param name="style">The line style the portrayal draws with.</param>
    /// <param name="label">The row's label.</param>
    /// <exception cref="KeyNotFoundException">The palette or symbol provider lacks one of the style's tokens or symbols.</exception>
    public LineLegendEntry Line(ComplexLineStyle style, string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new LineLegendEntry(Sample(style), label);
    }

    /// <summary>A line row for a simple line: one colour and width, solid or dashed.</summary>
    /// <param name="colorToken">The line's colour token.</param>
    /// <param name="width">The pen width in millimetres.</param>
    /// <param name="label">The row's label.</param>
    /// <param name="dashLength">The dash length in millimetres; 0 for a solid line.</param>
    /// <param name="gapLength">The gap between dashes in millimetres; 0 for a solid line.</param>
    /// <exception cref="KeyNotFoundException">The palette has no such token.</exception>
    public LineLegendEntry Line(string colorToken, double width, string label, double dashLength = 0, double gapLength = 0)
    {
        ArgumentNullException.ThrowIfNull(label);
        return new LineLegendEntry(Sample(colorToken, width, dashLength, gapLength), label);
    }

    /// <summary>A line sample of a complex line style, resolved for the builder's palette.</summary>
    /// <param name="style">The line style.</param>
    /// <exception cref="KeyNotFoundException">The palette or symbol provider lacks one of the style's tokens or symbols.</exception>
    public LegendLineSample Sample(ComplexLineStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var (interval, dashes, places) = style.Layout();

        var symbols = new LegendLineSymbol[places.Count];
        for (int i = 0; i < symbols.Length; i++)
        {
            var (reference, position, rotation) = places[i];
            var raw = RawSymbol(reference);
            var pivot = SvgPivotMetrics.TryParse(raw)?.RelativeOffset ?? (0.0, 0.0);
            symbols[i] = new LegendLineSymbol(SvgProcessor.Process(raw, _colors), position, rotation, pivot.X, pivot.Y);
        }

        return new LegendLineSample
        {
            Color = Color(style.ColorToken, null),
            Width = style.Width,
            Dashes = dashes,
            IntervalLength = interval,
            Symbols = symbols,
        };
    }

    /// <summary>A line sample of a simple line, resolved for the builder's palette.</summary>
    /// <param name="colorToken">The line's colour token.</param>
    /// <param name="width">The pen width in millimetres.</param>
    /// <param name="dashLength">The dash length in millimetres; 0 for a solid line.</param>
    /// <param name="gapLength">The gap between dashes in millimetres; 0 for a solid line.</param>
    /// <exception cref="KeyNotFoundException">The palette has no such token.</exception>
    public LegendLineSample Sample(string colorToken, double width, double dashLength = 0, double gapLength = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        return Sample(new ComplexLineStyle
        {
            Name = "legend",
            ColorToken = colorToken,
            Width = width,
            DashLength = dashLength,
            GapLength = gapLength,
        });
    }

    private string Color(string token, double? transparency)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (transparency is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(transparency), transparency, "Transparency must be between 0 and 1.");

        var color = _palette.Resolve(token, _paletteType);
        if (transparency is not { } t || t == 0)
            return color.ToHex();

        byte alpha = (byte)Math.Round(255 * (1 - t));
        return $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private string ResolveSymbol(string name) => SvgProcessor.Process(RawSymbol(name), _colors);

    private string RawSymbol(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_symbols is null)
            throw new InvalidOperationException($"The legend has no symbol provider to draw '{name}' from.");
        return _symbols(name)
            ?? throw new KeyNotFoundException($"The symbol provider has no symbol '{name}'.");
    }
}
