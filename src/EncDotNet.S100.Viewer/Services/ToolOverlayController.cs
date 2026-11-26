using EncDotNet.S100.Viewer.Tools;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Draws the Measure and Range &amp; bearing state on one map. The state is
/// geographic and shared, so every pane of a split has one of these and
/// shows the same measurement, wherever it was made (split map design note
/// D12, D27).
/// </summary>
/// <remarks>
/// Each overlay is drawn while its tool is on, as when the tool drew it
/// itself: the measure path while <see cref="MeasureSession.IsActive"/>, the
/// range and bearing while <see cref="RangeBearingState.IsToolActive"/>.
/// </remarks>
internal sealed class ToolOverlayController : IDisposable
{
    private readonly IMapLayerCollection _layers;
    private readonly MeasureSession _measure;
    private readonly RangeBearingState _rangeBearing;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly ViewerSettingsService _settings;
    private readonly Action<Action> _marshal;
    private readonly Action _requestRedraw;
    private readonly MemoryLayer _measureLayer = MeasureOverlayLayer.Create();
    private readonly MemoryLayer _rangeBearingLayer = RangeBearingOverlayLayer.Create();
    private bool _disposed;

    /// <param name="layers">The map's layers; the overlays go on its tool tier.</param>
    /// <param name="measure">The shared measure state.</param>
    /// <param name="rangeBearing">The shared range and bearing state.</param>
    /// <param name="appearance">Accent and theme for the overlays.</param>
    /// <param name="settings">The Distance unit for the range and bearing labels.</param>
    /// <param name="marshal">Runs work on the UI thread.</param>
    /// <param name="requestRedraw">Asks the map to repaint.</param>
    public ToolOverlayController(
        IMapLayerCollection layers,
        MeasureSession measure,
        RangeBearingState rangeBearing,
        IMeasureOverlayAppearanceProvider appearance,
        ViewerSettingsService settings,
        Action<Action> marshal,
        Action requestRedraw)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(rangeBearing);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(marshal);
        ArgumentNullException.ThrowIfNull(requestRedraw);
        _layers = layers;
        _measure = measure;
        _rangeBearing = rangeBearing;
        _appearance = appearance;
        _settings = settings;
        _marshal = marshal;
        _requestRedraw = requestRedraw;

        _measure.Changed += OnChanged;
        _rangeBearing.Changed += OnChanged;
        _rangeBearing.ToolActiveChanged += OnChanged;
        _appearance.Changed += OnChanged;
        _settings.DistanceUnitChanged += OnUnitChanged;

        _marshal(() =>
        {
            if (_disposed)
                return;
            _layers.AddToolLayer(_measureLayer);
            _layers.AddToolLayer(_rangeBearingLayer);
            Rebuild();
        });
    }

    private void OnChanged(object? sender, EventArgs e) => _marshal(Rebuild);

    private void OnUnitChanged(DistanceUnit unit) => _marshal(Rebuild);

    private void Rebuild()
    {
        if (_disposed)
            return;

        var appearance = _appearance.Current;
        if (_measure.IsActive)
            MeasureOverlayLayer.Update(_measureLayer, _measure.State, appearance, _measure.Unit);
        else
            Clear(_measureLayer);

        if (_rangeBearing.IsToolActive)
            RangeBearingOverlayLayer.Update(_rangeBearingLayer, _rangeBearing, appearance, _settings.DistanceUnit);
        else
            Clear(_rangeBearingLayer);

        _requestRedraw();
    }

    private static void Clear(MemoryLayer layer)
    {
        if (!layer.Features.Any())
            return;
        layer.Features = new List<IFeature>();
        layer.DataHasChanged();
    }

    /// <summary>Removes the overlays and unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _measure.Changed -= OnChanged;
        _rangeBearing.Changed -= OnChanged;
        _rangeBearing.ToolActiveChanged -= OnChanged;
        _appearance.Changed -= OnChanged;
        _settings.DistanceUnitChanged -= OnUnitChanged;
        _marshal(() =>
        {
            _layers.RemoveToolLayer(_measureLayer);
            _layers.RemoveToolLayer(_rangeBearingLayer);
        });
    }
}
