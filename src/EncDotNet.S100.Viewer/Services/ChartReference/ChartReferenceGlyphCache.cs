using Avalonia.Media;
using Avalonia.Media.Imaging;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Viewer.ViewModels.Legends;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// The glyphs Chart reference shows, drawn once per spec, item, palette and
/// size and kept in memory (symbol dictionary design note §3.4, R3). A glyph
/// is a catalogue item drawn fitted to its box by
/// <see cref="LegendGlyphRenderer.RenderFitted"/>: a 40 px tile or a large
/// hero glyph.
/// </summary>
/// <remarks>
/// <para>
/// As the Legend tab does (legends design note D10), the cache follows the
/// palette: on <see cref="ViewerSettingsService.PaletteChanged"/> it drops
/// every glyph and raises <see cref="PaletteChanged"/>, and the glyphs are
/// drawn again in the new palette as they are next asked for. A glyph is
/// drawn on a transparent background; a view shows it on <see cref="Tile"/>,
/// the palette's chart background.
/// </para>
/// <para>
/// Dropped bitmaps are not disposed, since a view may still show one until it
/// asks again; they go with the garbage collector.
/// </para>
/// </remarks>
internal sealed class ChartReferenceGlyphCache : IDisposable
{
    /// <summary>The device scale glyphs are drawn at, so they stay sharp on a high-density display.</summary>
    public const float DeviceScale = 2f;

    private readonly Dictionary<Key, IImage?> _glyphs = [];
    private readonly ViewerSettingsService? _settings;
    private PaletteType _palette;

    /// <summary>Creates a cache that follows the viewer's palette.</summary>
    /// <param name="settings">The viewer settings, whose palette the glyphs are drawn in.</param>
    public ChartReferenceGlyphCache(ViewerSettingsService settings)
        : this(settings?.SelectedPalette ?? throw new ArgumentNullException(nameof(settings)))
    {
        _settings = settings;
        _settings.PaletteChanged += SetPalette;
    }

    /// <summary>Creates a cache for <paramref name="palette"/>; <see cref="SetPalette"/> changes it.</summary>
    /// <param name="palette">The palette glyphs are drawn in.</param>
    internal ChartReferenceGlyphCache(PaletteType palette) => _palette = palette;

    /// <summary>Raised after the palette changed and the glyphs were dropped, so views ask for them again.</summary>
    public event EventHandler? PaletteChanged;

    /// <summary>The palette glyphs are drawn in.</summary>
    public PaletteType Palette
    {
        get
        {
            lock (_glyphs)
                return _palette;
        }
    }

    /// <summary>The tile a glyph sits on: the palette's chart background (legends D10).</summary>
    public IBrush Tile => LegendViewModel.TileOf(Palette);

    /// <summary>How many glyphs are held.</summary>
    public int Count
    {
        get
        {
            lock (_glyphs)
                return _glyphs.Count;
        }
    }

    /// <summary>
    /// The glyph of <paramref name="item"/> in <paramref name="spec"/> at
    /// <paramref name="width"/> × <paramref name="height"/> logical pixels in
    /// the current palette, drawn on first use from the row
    /// <paramref name="build"/> makes for that palette; <see langword="null"/>
    /// when <paramref name="build"/> gives no row (an item with nothing to draw).
    /// </summary>
    /// <param name="spec">The product, e.g. <c>"S-101"</c>.</param>
    /// <param name="item">
    /// The item, unique within the spec: qualify names that two kinds of item
    /// share, e.g. <c>"BCNLAT21"</c> for a symbol and <c>"fill:DRGARE01"</c> for an area fill.
    /// </param>
    /// <param name="width">The box width in logical pixels.</param>
    /// <param name="height">The box height in logical pixels.</param>
    /// <param name="build">
    /// Makes the item's legend row for a palette, as <c>CatalogueLegendBuilder</c>
    /// does with that palette's colours; called only on a miss.
    /// </param>
    public IImage? Get(string spec, string item, int width, int height, Func<PaletteType, LegendEntry?> build)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        ArgumentException.ThrowIfNullOrEmpty(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(build);

        PaletteType palette;
        lock (_glyphs)
            palette = _palette;

        var key = new Key(spec, item, palette, width, height);
        lock (_glyphs)
        {
            if (_glyphs.TryGetValue(key, out var cached))
                return cached;
        }

        var glyph = build(palette) is { } entry ? Render(entry, width, height) : null;
        lock (_glyphs)
        {
            // Kept only while its palette is current; one drawn across a
            // palette change is returned but not held.
            if (key.Palette == _palette)
                _glyphs.TryAdd(key, glyph);
            return _glyphs.TryGetValue(key, out var held) ? held : glyph;
        }
    }

    /// <summary>
    /// Follows a palette change: drops every glyph and raises
    /// <see cref="PaletteChanged"/>. Nothing happens when the palette is the same.
    /// </summary>
    /// <param name="palette">The new palette.</param>
    public void SetPalette(PaletteType palette)
    {
        lock (_glyphs)
        {
            if (_palette == palette)
                return;
            _palette = palette;
            _glyphs.Clear();
        }

        PaletteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_settings is not null)
            _settings.PaletteChanged -= SetPalette;
        lock (_glyphs)
            _glyphs.Clear();
    }

    private static Bitmap Render(LegendEntry entry, int width, int height)
    {
        using var bitmap = LegendGlyphRenderer.RenderFitted(entry, width, height, DeviceScale);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        return new Bitmap(stream);
    }

    private readonly record struct Key(string Spec, string Item, PaletteType Palette, int Width, int Height);
}
