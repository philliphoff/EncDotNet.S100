using System.Collections.Concurrent;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Renderers.Skia;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The legend graphics of the tiles <c>tiles serve</c> renders (issue #1501):
/// the rows the drawn sub-layers carry, gathered into one legend per product
/// as the viewer's Legend tab gathers them (<see cref="ProductLegends"/>), and
/// drawn as one PNG per style (<see cref="LegendSheetRenderer"/>). Each style
/// draws its rows in its own palette and with its own mariner settings, so
/// the S-102 shades and safety contour follow a registered settings set.
/// Thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// Rows come from the scene of the style at the default time step: what a
/// product's legend shows doesn't change with time. A catalogue product with
/// no legend of its own (S-101) has no rows here; the viewer counts the
/// symbols it draws in view, which a server has no view for.
/// </para>
/// <para>
/// The legends of a style are kept once built, so capabilities documents can
/// give each graphic's size without portraying again; a style whose legend
/// hasn't been built yet is given the default style's size, which a palette
/// alone never changes (labels don't depend on the palette).
/// </para>
/// </remarks>
internal sealed class TileLegend
{
    /// <summary>The only format legend graphics are drawn in.</summary>
    public const string Format = "image/png";

    private readonly Func<string?, Resolved?> _resolve;
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<ProductLegend>>> _legends = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (int Width, int Height)?> _sizes = new(StringComparer.Ordinal);

    /// <param name="resolve">
    /// Resolves a requested style (<see langword="null"/> for the default) to
    /// its cache key, palette and scene, or <see langword="null"/> for a style
    /// that isn't served.
    /// </param>
    public TileLegend(Func<string?, Resolved?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        _resolve = resolve;
    }

    /// <summary>A style resolved for its legend.</summary>
    /// <param name="Key">The key its legends are kept under: the same for styles with the same palette and settings.</param>
    /// <param name="Palette">Its palette: <c>day</c>, <c>dusk</c> or <c>night</c>.</param>
    /// <param name="Scene">Prepares (or finds) its scene at the default time step.</param>
    public sealed record Resolved(string Key, string Palette, Func<TileScene> Scene);

    /// <summary>How a legend graphic is drawn: the glyph size, in pixels at 96 DPI, and the DPI.</summary>
    /// <param name="GlyphWidth">The width of each row's glyph.</param>
    /// <param name="GlyphHeight">The height of each row's glyph.</param>
    /// <param name="Dpi">The display resolution; text and glyphs scale with it from 96.</param>
    public readonly record struct Options(
        int GlyphWidth = LegendSheetStyle.DefaultGlyphWidth,
        int GlyphHeight = LegendSheetStyle.DefaultGlyphHeight,
        double Dpi = WmsMapComposer.StandardDpi);

    /// <summary>Whether the default style's legend has any rows; without them, no legend is offered.</summary>
    public bool HasRows => Legends(null) is { } legends && legends.Any(l => l.Rows.Any());

    /// <summary>
    /// The product legends of <paramref name="style"/>, top of the stack
    /// first, built on first use; <see langword="null"/> for a style that
    /// isn't served. Building may portray, so call it off request threads.
    /// </summary>
    public IReadOnlyList<ProductLegend>? Legends(string? style)
    {
        if (_resolve(style) is not { } resolved)
            return null;
        return _legends.GetOrAdd(resolved.Key, _ => new Lazy<IReadOnlyList<ProductLegend>>(() => FromScene(resolved.Scene()))).Value;
    }

    /// <summary>
    /// The size of <paramref name="style"/>'s graphic with the default
    /// options, for capabilities documents: its own when its legend has been
    /// built, else the default style's. <see langword="null"/> when the style
    /// isn't served or there are no rows.
    /// </summary>
    public (int Width, int Height)? SizeOf(string? style)
    {
        if (_resolve(style) is not { } resolved)
            return null;
        var own = _legends.TryGetValue(resolved.Key, out var built) && built.IsValueCreated;
        var key = own ? resolved.Key : DefaultKey;
        // The size doesn't depend on the palette, so it's kept by legend.
        return _sizes.GetOrAdd(key, _ =>
        {
            var legends = own ? built!.Value : Legends(null);
            return legends is null || !legends.Any(l => l.Rows.Any())
                ? null
                : LegendSheetRenderer.Measure(legends, SheetStyle(resolved.Palette, default));
        });
    }

    private string DefaultKey => _resolve(null)?.Key ?? string.Empty;

    /// <summary>
    /// <paramref name="style"/>'s legend graphic as a PNG, or
    /// <see langword="null"/> for a style that isn't served.
    /// </summary>
    public byte[]? Render(string? style, Options options)
    {
        if (_resolve(style) is not { } resolved || Legends(style) is not { } legends)
            return null;

        using var bitmap = LegendSheetRenderer.Render(legends, SheetStyle(resolved.Palette, options));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The product legends a scene's sub-layers carry, top of the stack first:
    /// by S-98 display plane, then priority within the plane, then input order,
    /// highest first. Datasets that aren't painted are left out.
    /// </summary>
    internal static IReadOnlyList<ProductLegend> FromScene(TileScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var subLayers = new List<(int Plane, int Priority, int Index, int Order, SubLayerLegend Legend)>();
        for (int i = 0; i < scene.Inputs.Count; i++)
        {
            if (scene.Inputs[i] is not { Active: true } input)
                continue;
            if (input.Vector is { } vector)
            {
                for (int s = 0; s < vector.SubLayers.Count; s++)
                {
                    var sub = vector.SubLayers[s];
                    if (SubLayerLegend.Of(sub, vector.Spec.Name) is { } legend)
                        subLayers.Add(((int)sub.Plane, sub.WithinPlanePriority, i, s, legend));
                }
            }
            else if (input.Coverage is { } coverage)
            {
                for (int s = 0; s < coverage.SubLayers.Count; s++)
                {
                    var sub = coverage.SubLayers[s];
                    if (SubLayerLegend.Of(sub, coverage.Spec.Name) is { } legend)
                        subLayers.Add(((int)sub.Plane, sub.WithinPlanePriority, i, s, legend));
                }
            }
        }

        return ProductLegends.Build(subLayers
            .OrderByDescending(s => s.Plane)
            .ThenByDescending(s => s.Priority)
            .ThenByDescending(s => s.Index)
            .ThenByDescending(s => s.Order)
            .Select(s => s.Legend));
    }

    /// <summary>
    /// The sheet's colours in <paramref name="palette"/>: dark text on white by
    /// day, light text on dark at dusk and night, each glyph on the chart's
    /// background (S-101 <c>DEPDW</c>), as the viewer's Legend tab shows them (D10).
    /// </summary>
    internal static LegendSheetStyle SheetStyle(string palette, Options options)
    {
        var glyphWidth = options.GlyphWidth > 0 ? options.GlyphWidth : LegendSheetStyle.DefaultGlyphWidth;
        var glyphHeight = options.GlyphHeight > 0 ? options.GlyphHeight : LegendSheetStyle.DefaultGlyphHeight;
        var scale = (float)((options.Dpi > 0 ? options.Dpi : WmsMapComposer.StandardDpi) / WmsMapComposer.StandardDpi);
        return palette switch
        {
            "dusk" => new LegendSheetStyle
            {
                Background = new SKColor(0x26, 0x26, 0x26),
                Text = new SKColor(0xE6, 0xE6, 0xE6),
                SecondaryText = new SKColor(0xA6, 0xA6, 0xA6),
                GlyphTile = SKColors.Black,
                GlyphWidth = glyphWidth,
                GlyphHeight = glyphHeight,
                Scale = scale,
            },
            "night" => new LegendSheetStyle
            {
                Background = new SKColor(0x10, 0x10, 0x10),
                Text = new SKColor(0x9A, 0x9A, 0x9A),
                SecondaryText = new SKColor(0x6E, 0x6E, 0x6E),
                GlyphTile = SKColors.Black,
                GlyphWidth = glyphWidth,
                GlyphHeight = glyphHeight,
                Scale = scale,
            },
            _ => new LegendSheetStyle
            {
                GlyphTile = new SKColor(201, 237, 255),
                GlyphWidth = glyphWidth,
                GlyphHeight = glyphHeight,
                Scale = scale,
            },
        };
    }
}
