using System.Globalization;
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
    private IReadOnlyDictionary<string, string>? _colourNames;

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

                case LeadGlyphKind.Area when glyph.AreaToken is { } token:
                    return new SymbolLegendEntry(Patch(Resolve(colors, token), colors.TryResolve("CHGRD", out var edge) ? edge : "#808080"), label);

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
    /// A small area patch, as the Legend shows an area: a rounded square
    /// filled with <paramref name="fill"/> and edged with
    /// <paramref name="edge"/>, as a symbol SVG so a tile keeps it inset (#1650).
    /// </summary>
    /// <param name="fill">The fill colour as a hex string.</param>
    /// <param name="edge">The edge colour as a hex string.</param>
    /// <returns>The SVG.</returns>
    internal static string Patch(string fill, string edge)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ArgumentNullException.ThrowIfNull(edge);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"12mm\" height=\"12mm\" viewBox=\"0 0 12 12\"><rect x=\"0.3\" y=\"0.3\" width=\"11.4\" height=\"11.4\" rx=\"1.6\" fill=\"{fill}\" stroke=\"{edge}\" stroke-width=\"0.6\" /></svg>");
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

    /// <summary>
    /// A colour token's hex colour in <paramref name="palette"/>, from the
    /// catalogue's colour profile, such as <c>DEPDW</c> for the chart's deep
    /// water; <see langword="null"/> when the profile doesn't define it.
    /// </summary>
    /// <param name="token">The colour token.</param>
    /// <param name="palette">The palette.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<string?> ColorAsync(string token, PaletteType palette, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PaletteAsync(palette, cancellationToken).ConfigureAwait(false) is { } colors && colors.TryResolve(token, out var hex)
                ? hex
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// What a colour token stands for, from the catalogue's colour profile:
    /// its description, such as "Very shallow water" for <c>DEPVS</c>, as it
    /// is written there; <see langword="null"/> when the profile gives none.
    /// </summary>
    /// <param name="token">The colour token.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<string?> ColourNameAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _colourNames ??= await ReadColourNamesAsync(cancellationToken).ConfigureAwait(false);
            return _colourNames.GetValueOrDefault(token);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Every profile's descriptions; the first that names a token wins.
    private async Task<IReadOnlyDictionary<string, string>> ReadColourNamesAsync(CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            try
            {
                using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
                foreach (var (token, name) in ColorProfileReader.ReadDescriptions(stream))
                    names.TryAdd(token, name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A profile that can't be read names nothing.
            }
        }

        return names;
    }

    /// <summary>
    /// The size of symbol <paramref name="id"/> on the chart, in millimetres,
    /// from its SVG's <c>width</c> and <c>height</c> (<c>"1.65mm"</c>), else
    /// its <c>viewBox</c>, whose units are millimetres in S-100 symbols; for
    /// the Reference tab's glyph at chart size (R7, #1650).
    /// </summary>
    /// <param name="id">The symbol id, e.g. <c>BCNLAT21</c>.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The width and height in millimetres, or <see langword="null"/> when the catalogue lacks the symbol or a size.</returns>
    public async Task<(double Width, double Height)?> SymbolSizeAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SymbolAsync(id, cancellationToken).ConfigureAwait(false) is { } svg ? SizeOf(svg) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The millimetre size an S-100 symbol's SVG declares, or <see langword="null"/>.</summary>
    /// <param name="svg">The SVG text.</param>
    internal static (double Width, double Height)? SizeOf(string svg)
    {
        System.Xml.Linq.XElement root;
        try
        {
            root = System.Xml.Linq.XDocument.Parse(svg).Root!;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        static double? Millimetres(string? value) =>
            value is { } v && v.EndsWith("mm", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mm)
            && mm > 0
                ? mm
                : null;

        if (Millimetres((string?)root.Attribute("width")) is { } w && Millimetres((string?)root.Attribute("height")) is { } h)
            return (w, h);
        var box = ((string?)root.Attribute("viewBox"))?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (box is { Length: 4 }
            && double.TryParse(box[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw)
            && double.TryParse(box[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bh)
            && bw > 0 && bh > 0)
        {
            return (bw, bh);
        }

        return null;
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
