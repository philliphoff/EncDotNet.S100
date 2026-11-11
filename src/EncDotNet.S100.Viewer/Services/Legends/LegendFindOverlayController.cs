using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// Draws <see cref="LegendFindService.Current"/> on the map's overlay tier:
/// a ring around each feature a hovered Legend row describes (D19, #1412),
/// in the pick highlight's accent and casing.
/// </summary>
internal sealed class LegendFindOverlayController : IDisposable
{
    /// <summary>The overlay layer's name.</summary>
    public const string LayerName = "Legend Find";

    private readonly IMapLayerCollection _layers;
    private readonly LegendFindService _find;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly SettingsViewModel _settings;
    private readonly Action<Action> _marshal;
    private readonly MemoryLayer _layer;
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
        SettingsViewModel settings,
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

        _find.Changed += OnChanged;
        _appearance.Changed += OnChanged;
        _settings.PaletteChanged += OnPaletteChanged;
        _marshal(() =>
        {
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
        PickHighlightOverlayLayer.UpdateRings(
            _layer,
            _find.Current?.Rings ?? [],
            new PickHighlightAppearance(_appearance.Current.Accent, dark));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _find.Changed -= OnChanged;
        _appearance.Changed -= OnChanged;
        _settings.PaletteChanged -= OnPaletteChanged;
        _marshal(() => _layers.RemoveOverlayLayer(_layer));
    }
}
