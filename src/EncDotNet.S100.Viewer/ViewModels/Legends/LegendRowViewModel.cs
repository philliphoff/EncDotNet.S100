using Avalonia.Media;
using Avalonia.Media.Imaging;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.ViewModels.Legends;

/// <summary>
/// One legend row: a glyph and its label (legends design note D11: every row
/// has a label, never colour alone). A plain colour band shows as a swatch
/// (<see cref="Swatch"/>); an outlined area, a symbol, a line sample or an
/// area fill pattern as a
/// picture drawn by <see cref="Renderers.Skia.LegendGlyphRenderer"/>
/// (<see cref="Glyph"/>). Either sits on a tile of the palette's chart
/// background (<see cref="Tile"/>, D10).
/// </summary>
internal sealed class LegendRowViewModel : ViewModelBase
{
    /// <summary>
    /// How much larger than <see cref="Renderers.Skia.LegendGlyphRenderer.SizeOf"/>
    /// the tab shows a glyph, to fill the 42 × 20 tile: a symbol 18 × 18, a
    /// line 35 × 13.
    /// </summary>
    internal const float GlyphZoom = 1.25f;

    /// <summary>The device scale glyphs are drawn at, so they stay sharp on a high-density display.</summary>
    private const float GlyphDeviceScale = 2f * GlyphZoom;

    private readonly LegendEntry _entry;
    private Bitmap? _glyph;
    private bool _isHighlighted;

    /// <summary>Creates a legend row.</summary>
    /// <param name="entry">The legend row of the sub-layer.</param>
    /// <param name="tile">The palette's chart background, which the glyph sits on.</param>
    /// <param name="referenceSpec">
    /// The product whose Chart reference a catalogue key row's symbol is in
    /// (<see cref="LegendSection.CatalogueSpec"/>), so its label is a
    /// term (D7, symbol dictionary R8); <see langword="null"/> for none.
    /// </param>
    public LegendRowViewModel(LegendEntry entry, IBrush tile, string? referenceSpec = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Label);
        ArgumentNullException.ThrowIfNull(tile);
        _entry = entry;
        Tile = tile;
        Label = entry.Label;
        Kind = LegendRows.KindOf(entry);
        Color = entry is SwatchLegendEntry swatch ? swatch.Color : null;
        Swatch = Color is not null && Avalonia.Media.Color.TryParse(Color, out var parsed)
            ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(parsed)
            : Brushes.Transparent;
        var (width, height) = Renderers.Skia.LegendGlyphRenderer.SizeOf(entry);
        GlyphWidth = (int)MathF.Round(width * GlyphZoom, MidpointRounding.AwayFromZero);
        GlyphHeight = (int)MathF.Round(height * GlyphZoom, MidpointRounding.AwayFromZero);
        TermKey = entry is SymbolLegendEntry { SymbolName: { Length: > 0 } symbol }
            && referenceSpec is not null
            && SpecName.TryNormalize(referenceSpec, out var spec)
                ? new ReferenceKey(new SpecRef(spec, default), ReferenceKind.Symbol, symbol)
                : null;
    }

    /// <summary>
    /// The Chart reference entry a catalogue key row shows (D7, symbol
    /// dictionary R8, #1651): its label is a term. <see langword="null"/> for
    /// any other row, whose label is plain text.
    /// </summary>
    public ReferenceKey? TermKey { get; }

    /// <summary>Whether the label is a term, which takes keyboard focus for the row.</summary>
    public bool HasTerm => TermKey is not null;

    /// <summary>Whether the row itself takes keyboard focus: its label isn't a term.</summary>
    public bool IsRowFocusable => TermKey is null;

    /// <summary>The row this view model shows.</summary>
    public LegendEntry Entry => _entry;

    /// <summary>What the row's glyph shows.</summary>
    public LegendRowKind Kind { get; }

    /// <summary>The swatch colour as a hex string, or <see langword="null"/> for a symbol or line row.</summary>
    public string? Color { get; }

    /// <summary>The row label; also the row's accessible name.</summary>
    public string Label { get; }

    /// <summary>The swatch brush; transparent for a row without a colour.</summary>
    public IBrush Swatch { get; }

    /// <summary>The palette's chart background, under the swatch or glyph (D10).</summary>
    public IBrush Tile { get; }

    /// <summary>Whether the row is a plain colour swatch, drawn as a bordered box.</summary>
    public bool IsSwatch => Kind == LegendRowKind.Swatch;

    /// <summary>Whether the row shows a drawn glyph: an area, a symbol, a line sample or a fill pattern.</summary>
    public bool HasGlyph => !IsSwatch;

    /// <summary>The glyph's width in logical pixels, as shown (<see cref="GlyphZoom"/>).</summary>
    public int GlyphWidth { get; }

    /// <summary>The glyph's height in logical pixels, as shown (<see cref="GlyphZoom"/>).</summary>
    public int GlyphHeight { get; }

    /// <summary>
    /// The glyph picture of an area, symbol, line or fill row, drawn on first use;
    /// <see langword="null"/> for a plain swatch.
    /// </summary>
    public IImage? Glyph => IsSwatch ? null : _glyph ??= RenderGlyph(_entry);

    /// <summary>
    /// Whether the row is ringed: the row a <see cref="LegendService.Show"/>
    /// request named (the pick's <b>In legend</b>, D16).
    /// </summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => SetProperty(ref _isHighlighted, value);
    }

    private static Bitmap RenderGlyph(LegendEntry entry)
    {
        using var bitmap = Renderers.Skia.LegendGlyphRenderer.Render(entry, GlyphDeviceScale);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        return new Bitmap(stream);
    }
}
