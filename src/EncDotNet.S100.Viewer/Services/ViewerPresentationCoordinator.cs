using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Translates Viewer setting changes into explicit map presentation
/// applications.
/// </summary>
/// <remarks>
/// The application service provider owns this singleton for the Viewer
/// lifetime. It owns only the input subscriptions; dataset processors and
/// rendered layers remain owned by the injected
/// <see cref="IMapPresentationController"/>.
/// </remarks>
internal sealed class ViewerPresentationCoordinator : IDisposable
{
    private readonly SettingsViewModel _settings;
    private readonly EcdisDisplayState _ecdisDisplay;
    private readonly IMarinerSettingsProvider _marinerSettings;
    private readonly MapPresentationStateProjection _projection;
    private readonly IMapPresentationController _controller;
    private readonly IDepthAdjustmentRequests _depthAdjustment;

    public ViewerPresentationCoordinator(
        SettingsViewModel settings,
        EcdisDisplayState ecdisDisplay,
        IMarinerSettingsProvider marinerSettings,
        MapPresentationStateProjection projection,
        IMapPresentationController controller,
        IDepthAdjustmentRequests depthAdjustment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(marinerSettings);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(depthAdjustment);

        _settings = settings;
        _ecdisDisplay = ecdisDisplay;
        _marinerSettings = marinerSettings;
        _projection = projection;
        _controller = controller;
        _depthAdjustment = depthAdjustment;

        _settings.PaletteChanged += OnPaletteChanged;
        _settings.DisplayScaleChanged += OnPresentationChanged;
        _settings.TimeFormatChanged += OnTimeFormatChanged;
        _ecdisDisplay.Changed += OnPresentationChanged;
        _marinerSettings.Changed += OnMarinerChanged;
        _depthAdjustment.RequestChanged += OnDepthAdjustmentRequested;
    }

    public void Dispose()
    {
        _settings.PaletteChanged -= OnPaletteChanged;
        _settings.DisplayScaleChanged -= OnPresentationChanged;
        _settings.TimeFormatChanged -= OnTimeFormatChanged;
        _ecdisDisplay.Changed -= OnPresentationChanged;
        _marinerSettings.Changed -= OnMarinerChanged;
        _depthAdjustment.RequestChanged -= OnDepthAdjustmentRequested;
    }

    private void OnPaletteChanged(PaletteType _) => ApplyCurrent();

    private void OnMarinerChanged(MarinerSettings _) => ApplyCurrent();

    // Times drawn on the chart (a cyclone's forecast times) follow the
    // Local/UTC preference.
    private void OnTimeFormatChanged(TimeFormat _) => ApplyCurrent();

    private void OnPresentationChanged() => ApplyCurrent();

    private void ApplyCurrent()
    {
        var adjustment = _depthAdjustment.Requested;
        var presentation = _projection.CreateSnapshot().WithDepthAdjustment(adjustment);
        _ = ApplyCurrentAsync(presentation, adjustment);
    }

    private async Task ApplyCurrentAsync(MapPresentationState presentation, IDepthAdjustment? adjustment)
    {
        try
        {
            await _controller.SetPresentationAsync(presentation).ConfigureAwait(true);
            _depthAdjustment.MarkApplied(adjustment);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to apply map presentation:\n{ex}");
        }
    }

    // Chart-wide water level adjustment (#1132): only the depth layers
    // re-draw, and the indicator moves to the new time once they have.
    private void OnDepthAdjustmentRequested(object? sender, EventArgs e)
    {
        var adjustment = _depthAdjustment.Requested;
        var presentation = _projection.CreateSnapshot().WithDepthAdjustment(adjustment);
        _ = ApplyDepthAdjustmentAsync(presentation, adjustment);
    }

    private async Task ApplyDepthAdjustmentAsync(MapPresentationState presentation, IDepthAdjustment? adjustment)
    {
        try
        {
            if (await _controller.SetDepthAdjustmentAsync(presentation).ConfigureAwait(true))
                _depthAdjustment.MarkApplied(adjustment);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to apply water level adjustment:\n{ex}");
        }
    }
}
