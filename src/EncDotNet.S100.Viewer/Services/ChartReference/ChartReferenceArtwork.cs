using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// Reads the catalogue artwork a Chart reference tile needs, the palettes,
/// symbols, line styles and area fills, from one product's portrayal
/// catalogue, and makes the legend row <see cref="ChartReferenceGlyphCache"/>
/// draws for a <see cref="LeadGlyph"/> (#1637).
/// </summary>
/// <remarks>
/// Each asset is read once and kept. The methods may be called from any
/// thread; reads run on the caller's.
/// </remarks>
internal sealed class ChartReferenceArtwork
{
    // S-52's dash and dot patterns, in millimetres: a 3.6 mm dash with a
    // 1.8 mm gap, and a 0.6 mm dot with a 1.2 mm gap.
    private const double DashLength = 3.6;
    private const double DashGap = 1.8;
    private const double DotLength = 0.6;
    private const double DotGap = 1.2;

    private readonly PortrayalCatalogueProvider _provider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<PaletteType, ColorPalette> _palettes = [];
    private readonly Dictionary<string, string> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LineStyle> _lineStyles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AreaFill> _areaFills = new(StringComparer.OrdinalIgnoreCase);
    private bool _palettesLoaded;

    /// <summary>Creates the artwork reader over a product's portrayal catalogue.</summary>
    /// <param name="provider">The product's portrayal catalogue.</param>
    public ChartReferenceArtwork(PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>
    /// The legend row for <paramref name="glyph"/> in <paramref name="palette"/>,
    /// reading the assets it needs; <see langword="null"/> when there is
    /// nothing to draw or the catalogue lacks an asset.
    /// </summary>
    /// <param name="glyph">The glyph to draw.</param>
    /// <param name="palette">The palette.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The legend row, labelled with the glyph's key.</returns>
    public async Task<LegendEntry?> BuildAsync(LeadGlyph glyph, PaletteType palette, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        if (!glyph.HasGlyph)
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var colors = await PaletteAsync(palette, cancellationToken).ConfigureAwait(false);
            if (colors is null)
                return null;
            var label = glyph.Key.Code;

            switch (glyph.Kind)
            {
                case LeadGlyphKind.Symbol when glyph.ItemId is { } id:
                    {
                        var svg = await SymbolAsync(id, cancellationToken).ConfigureAwait(false);
                        return svg is null ? null : Builder(colors).Symbol(id, label);
                    }

                case LeadGlyphKind.LineStyle when glyph.ItemId is { } id:
                    {
                        if (await LineStyleAsync(id, cancellationToken).ConfigureAwait(false) is not { } style)
                            return null;
                        foreach (var symbol in Flatten(style).SelectMany(s => s.Symbols))
                        {
                            if (await SymbolAsync(symbol.Reference, cancellationToken).ConfigureAwait(false) is null)
                                return null;
                        }

                        return Builder(colors).Line(id, label);
                    }

                case LeadGlyphKind.AreaFill when glyph.ItemId is { } id:
                    {
                        if (await AreaFillAsync(id, cancellationToken).ConfigureAwait(false) is not { } fill)
                            return null;
                        if (fill.PatternSymbol is { Length: > 0 } pattern
                            && await SymbolAsync(pattern, cancellationToken).ConfigureAwait(false) is null)
                        {
                            return null;
                        }

                        return Builder(colors).Fill(id, label);
                    }

                case LeadGlyphKind.Stripes when glyph.Tokens is { Count: > 0 } tokens:
                    return new SymbolLegendEntry(Stripes(tokens.Select(t => Resolve(colors, t)).ToList()), label);

                case LeadGlyphKind.Pen when glyph.Pen is { } pen:
                    return new LineLegendEntry(Sample(pen, Resolve(colors, pen.Token)), label);

                default:
                    return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Square stripes, one per colour, left to right, as a symbol SVG so it
    /// fills a fitted tile.
    /// </summary>
    /// <param name="colors">The colours as hex strings.</param>
    /// <returns>The SVG.</returns>
    internal static string Stripes(IReadOnlyList<string> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        const double size = 12;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}mm\" height=\"{size}mm\" viewBox=\"0 0 {size} {size}\">");
        double width = size / Math.Max(1, colors.Count);
        for (int i = 0; i < colors.Count; i++)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<rect x=\"{i * width:0.###}\" y=\"0\" width=\"{width:0.###}\" height=\"{size}\" fill=\"{colors[i]}\" />");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static LegendLineSample Sample(PenUse pen, string color)
    {
        var (dashes, interval) = pen.Dash.ToLowerInvariant() switch
        {
            "dash" or "dashed" => (new[] { (0.0, DashLength) }, DashLength + DashGap),
            "dot" or "dotted" => (new[] { (0.0, DotLength) }, DotLength + DotGap),
            _ => (Array.Empty<(double, double)>(), 0.0),
        };
        return new LegendLineSample
        {
            Color = color,
            Width = pen.Width > 0 ? pen.Width : 0.32,
            Dashes = dashes,
            IntervalLength = interval,
        };
    }

    private static string Resolve(ColorPalette palette, string token) =>
        token.StartsWith('#') ? token : palette.Resolve(token);

    private static IEnumerable<LineStyle> Flatten(LineStyle style) =>
        style.Components.Count > 0 ? style.Components.SelectMany(Flatten) : [style];

    private CatalogueLegendBuilder Builder(ColorPalette palette) => new(
        palette,
        name => _symbols.GetValueOrDefault(name),
        name => _lineStyles.GetValueOrDefault(name),
        name => _areaFills.GetValueOrDefault(name));

    private async Task<ColorPalette?> PaletteAsync(PaletteType type, CancellationToken cancellationToken)
    {
        if (!_palettesLoaded)
        {
            await LoadPalettesAsync(cancellationToken).ConfigureAwait(false);
            _palettesLoaded = true;
        }

        return _palettes.TryGetValue(type, out var palette) ? palette
            : _palettes.TryGetValue(PaletteType.Day, out var day) ? day
            : null;
    }

    // As the portrayal catalogues read them: a profile per palette named
    // after it, or every palette in one file.
    private async Task LoadPalettesAsync(CancellationToken cancellationToken)
    {
        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            var name = string.IsNullOrEmpty(item.Description.Name)
                ? Path.GetFileNameWithoutExtension(item.FileName)
                : item.Description.Name;
            PaletteType? type = name switch
            {
                _ when name.Contains("Day", StringComparison.OrdinalIgnoreCase) => PaletteType.Day,
                _ when name.Contains("Dusk", StringComparison.OrdinalIgnoreCase) => PaletteType.Dusk,
                _ when name.Contains("Night", StringComparison.OrdinalIgnoreCase) => PaletteType.Night,
                _ => null,
            };
            var wanted = type is { } t ? [(t, name)] : new[] { (PaletteType.Day, "Day"), (PaletteType.Dusk, "Dusk"), (PaletteType.Night, "Night") };
            foreach (var (paletteType, paletteName) in wanted)
            {
                if (_palettes.ContainsKey(paletteType))
                    continue;
                try
                {
                    using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
                    var palette = ColorProfileReader.Read(stream, paletteName);
                    if (palette.Colors.Count > 0)
                        _palettes[paletteType] = palette;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A profile that can't be read leaves its palette out.
                }
            }
        }
    }

    private async Task<string?> SymbolAsync(string id, CancellationToken cancellationToken)
    {
        if (_symbols.TryGetValue(id, out var svg))
            return svg;
        var item = _provider.Catalogue.Symbols.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return null;
        try
        {
            using var stream = await _provider.FetchAssetAsync(item, "Symbols", cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            svg = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or KeyNotFoundException or FileNotFoundException)
        {
            return null;
        }

        _symbols[id] = svg;
        return svg;
    }

    private async Task<LineStyle?> LineStyleAsync(string id, CancellationToken cancellationToken)
    {
        if (_lineStyles.TryGetValue(id, out var style))
            return style;
        var item = _provider.Catalogue.LineStyles.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return null;
        try
        {
            using var stream = await _provider.FetchAssetAsync(item, "LineStyles", cancellationToken).ConfigureAwait(false);
            style = LineStyleReader.Read(stream, id);
        }
        catch (Exception ex) when (ex is IOException or KeyNotFoundException or System.Xml.XmlException)
        {
            return null;
        }

        _lineStyles[id] = style;
        return style;
    }

    private async Task<AreaFill?> AreaFillAsync(string id, CancellationToken cancellationToken)
    {
        if (_areaFills.TryGetValue(id, out var fill))
            return fill;
        var item = _provider.Catalogue.AreaFills.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return null;
        try
        {
            using var stream = await _provider.FetchAssetAsync(item, "AreaFills", cancellationToken).ConfigureAwait(false);
            fill = AreaFillReader.Read(stream, id);
        }
        catch (Exception ex) when (ex is IOException or KeyNotFoundException or System.Xml.XmlException)
        {
            return null;
        }

        _areaFills[id] = fill;
        return fill;
    }
}
