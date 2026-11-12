using System.Runtime.InteropServices;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Tools;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// Draws <see cref="LegendFindService.Current"/> on the map's overlay tier:
/// a ring around each feature a hovered Legend row describes (D19, #1412),
/// in the pick highlight's accent and casing, or for a band of a colour or
/// arrow ramp, an accent tint over the band's area (#1433).
/// </summary>
internal sealed class LegendFindOverlayController : IDisposable
{
    /// <summary>The overlay layer's name.</summary>
    public const string LayerName = "Legend Find";

    /// <summary>The band tint layer's name.</summary>
    public const string TintLayerName = "Legend Find Tint";

    // The tint: a wash inside the band, a solid accent edge, and outside it a
    // light casing (grey on a dark chart) so the edge reads over a band
    // colour close to the accent, as the rings' casing does.
    private const byte FillAlpha = 96;
    private const byte DarkFillAlpha = 72;
    private const byte EdgeAlpha = 255;
    private const byte CasingAlpha = 220;

    private readonly IMapLayerCollection _layers;
    private readonly LegendFindService _find;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly ViewerSettingsService _settings;
    private readonly Action<Action> _marshal;
    private readonly MemoryLayer _layer;
    private readonly MemoryLayer _tint;
    private bool _disposed;

    /// <summary>Creates the controller and adds its layer once the map is ready.</summary>
    /// <param name="layers">The map's layers.</param>
    /// <param name="find">What is ringed.</param>
    /// <param name="appearance">The accent colour.</param>
    /// <param name="settings">The chart palette, which dims the casing on a dark chart.</param>
    /// <param name="marshal">Runs work on the UI thread.</param>
    public LegendFindOverlayController(
        IMapLayerCollection layers,
        LegendFindService find,
        IMeasureOverlayAppearanceProvider appearance,
        ViewerSettingsService settings,
        Action<Action> marshal)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(marshal);
        _layers = layers;
        _find = find;
        _appearance = appearance;
        _settings = settings;
        _marshal = marshal;
        _layer = PickHighlightOverlayLayer.Create();
        _layer.Name = LayerName;
        _tint = new MemoryLayer { Name = TintLayerName, Style = null, Features = new List<IFeature>() };

        _find.Changed += OnChanged;
        _appearance.Changed += OnChanged;
        _settings.PaletteChanged += OnPaletteChanged;
        _marshal(() =>
        {
            // The tint goes under the rings.
            _layers.AddOverlayLayer(_tint);
            _layers.AddOverlayLayer(_layer);
            Rebuild();
        });
    }

    private void OnChanged(object? sender, EventArgs e) => _marshal(Rebuild);

    private void OnPaletteChanged(PaletteType palette) => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed)
            return;
        var dark = _settings.SelectedPalette is PaletteType.Dusk or PaletteType.Night;
        var appearance = new PickHighlightAppearance(_appearance.Current.Accent, dark);
        PickHighlightOverlayLayer.UpdateRings(_layer, _find.Current?.Rings ?? [], appearance);
        UpdateTint(_tint, _find.Current?.Tint, appearance);
    }

    /// <summary>
    /// Replaces <paramref name="layer"/>'s features with <paramref name="tint"/>
    /// drawn as one raster over its view: an accent wash inside the band, a
    /// solid accent edge and a light casing just outside it. Null or an empty
    /// tint clears the layer.
    /// </summary>
    /// <param name="layer">The tint layer.</param>
    /// <param name="tint">The band's area, or null.</param>
    /// <param name="appearance">The accent, and whether the chart is dark.</param>
    internal static void UpdateTint(MemoryLayer layer, LegendBandTint? tint, PickHighlightAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (tint is not { TintedCells: > 0 })
        {
            layer.Features = new List<IFeature>();
            layer.DataHasChanged();
            return;
        }

        var accent = appearance.Accent;
        var fill = Pack(accent.R, accent.G, accent.B, appearance.IsDarkBasemap ? DarkFillAlpha : FillAlpha);
        var edge = Pack(accent.R, accent.G, accent.B, EdgeAlpha);
        var casing = appearance.IsDarkBasemap ? Pack(150, 150, 150, CasingAlpha) : Pack(255, 255, 255, CasingAlpha);
        int width = tint.Width, height = tint.Height;
        var pixels = new uint[width * height];
        // Off the mask counts as tinted for the edge, so the view's own edge
        // isn't outlined, and as untinted for the casing.
        bool Tinted(int i, int j, bool offMask) =>
            i < 0 || j < 0 || i >= width || j >= height ? offMask : tint.IsTinted(i, j);
        for (var j = 0; j < height; j++)
        {
            for (var i = 0; i < width; i++)
            {
                if (tint.IsTinted(i, j))
                {
                    var inside = Tinted(i - 1, j, true) && Tinted(i + 1, j, true) && Tinted(i, j - 1, true) && Tinted(i, j + 1, true);
                    pixels[(j * width) + i] = inside ? fill : edge;
                }
                else if (Tinted(i - 1, j, false) || Tinted(i + 1, j, false) || Tinted(i, j - 1, false) || Tinted(i, j + 1, false))
                {
                    pixels[(j * width) + i] = casing;
                }
            }
        }

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var view = tint.View;
        layer.Features = new List<IFeature>
        {
            new RasterFeature(new MRaster(png.ToArray(), new MRect(view.MinX, view.MinY, view.MaxX, view.MaxY)))
            {
                Styles = { new RasterStyle() },
            },
        };
        layer.DataHasChanged();
    }

    // RGBA8888 in memory order (R first) on a little-endian machine.
    private static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | (g << 8) | (b << 16) | (a << 24));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _find.Changed -= OnChanged;
        _appearance.Changed -= OnChanged;
        _settings.PaletteChanged -= OnPaletteChanged;
        _marshal(() =>
        {
            _layers.RemoveOverlayLayer(_layer);
            _layers.RemoveOverlayLayer(_tint);
        });
    }
}
