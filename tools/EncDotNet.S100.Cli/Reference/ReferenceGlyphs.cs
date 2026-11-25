using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Reference;

/// <summary>
/// Draws the glyphs of an exported chart reference (R14, #1723) as PNG
/// files, through <see cref="LegendGlyphRenderer"/> with the legend rows
/// <see cref="ChartReferenceArtwork"/> builds, as the viewer's Chart
/// reference tiles are drawn. Each glyph is drawn once per palette, on first
/// use, and its path kept.
/// </summary>
/// <remarks>
/// Glyphs are drawn on a transparent background; the page puts them on the
/// palette's chart background (<c>DEPDW</c>), as the viewer's tiles do.
/// </remarks>
internal sealed class ReferenceGlyphs
{
    /// <summary>The glyph box, in CSS pixels; the files hold twice as many, for sharp high-density screens.</summary>
    public const int Size = 48;

    /// <summary>Device pixels per CSS pixel in the files.</summary>
    public const float DeviceScale = 2f;

    private readonly ChartReferenceArtwork _artwork;
    private readonly string _root;
    private readonly Dictionary<string, string?> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Token, PaletteType Palette), string?> _colours = [];

    /// <summary>Creates the store.</summary>
    /// <param name="artwork">The product's catalogue artwork.</param>
    /// <param name="root">The export folder.</param>
    public ReferenceGlyphs(ChartReferenceArtwork artwork, string root)
    {
        _artwork = artwork;
        _root = root;
    }

    /// <summary>How many glyph files have been written.</summary>
    public int Count => _paths.Values.Count(p => p is not null);

    /// <summary>The palette's folder name.</summary>
    /// <param name="palette">The palette.</param>
    public static string Name(PaletteType palette) => palette switch
    {
        PaletteType.Dusk => "dusk",
        PaletteType.Night => "night",
        _ => "day",
    };

    /// <summary>
    /// The glyph's file, relative to the export folder, drawn now if it
    /// hasn't been; <see langword="null"/> when there is nothing to draw.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="palette">The palette.</param>
    /// <param name="cancellationToken">Cancels the read of catalogue assets.</param>
    public async Task<string?> PathAsync(LeadGlyph glyph, PaletteType palette, CancellationToken cancellationToken)
    {
        if (!glyph.HasGlyph)
            return null;

        var (group, name) = glyph.Kind switch
        {
            LeadGlyphKind.Symbol => ("symbols", glyph.ItemId ?? glyph.Key.Code),
            LeadGlyphKind.LineStyle => ("line-styles", glyph.ItemId ?? glyph.Key.Code),
            LeadGlyphKind.AreaFill => ("area-fills", glyph.ItemId ?? glyph.Key.Code),
            LeadGlyphKind.Area => ("areas", glyph.AreaToken ?? glyph.Key.Code),
            _ => ("lines", PenName(glyph)),
        };
        var path = ReferenceSite.GlyphOf(Name(palette), group, name);
        if (_paths.TryGetValue(path, out var known))
            return known;

        string? written = null;
        if (await _artwork.BuildAsync(glyph, palette, cancellationToken).ConfigureAwait(false) is { } row)
        {
            var padding = Size * 0.14f;
            using var bitmap = LegendGlyphRenderer.RenderFitted(row, Size, Size, DeviceScale, padding);
            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, png.ToArray(), cancellationToken).ConfigureAwait(false);
            written = path;
        }

        _paths[path] = written;
        return written;
    }

    /// <summary>A colour token's value in the palette, such as <c>#C9EDFF</c>, or <see langword="null"/>.</summary>
    /// <param name="token">The colour token, such as <c>DEPDW</c>.</param>
    /// <param name="palette">The palette.</param>
    /// <param name="cancellationToken">Cancels the read of the colour profile.</param>
    public async Task<string?> ColourAsync(string token, PaletteType palette, CancellationToken cancellationToken)
    {
        if (_colours.TryGetValue((token, palette), out var known))
            return known;
        var hex = await _artwork.ColorAsync(token, palette, cancellationToken).ConfigureAwait(false);
        _colours[(token, palette)] = hex;
        return hex;
    }

    // A plain line by its look, so lines that look alike share a file.
    private static string PenName(LeadGlyph glyph) =>
        glyph.Pen is { } pen
            ? string.Create(CultureInfo.InvariantCulture, $"{pen.Token}_{pen.Width:0.###}_{pen.Dash}")
            : glyph.Key.Code;
}
