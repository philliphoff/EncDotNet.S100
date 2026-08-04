using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
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

    public ViewerPresentationCoordinator(
        SettingsViewModel settings,
        EcdisDisplayState ecdisDisplay,
        IMarinerSettingsProvider marinerSettings,
        MapPresentationStateProjection projection,
        IMapPresentationController controller)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(marinerSettings);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(controller);

        _settings = settings;
        _ecdisDisplay = ecdisDisplay;
        _marinerSettings = marinerSettings;
        _projection = projection;
        _controller = controller;

        _settings.PaletteChanged += OnPaletteChanged;
        _settings.DisplayScaleChanged += OnPresentationChanged;
        _ecdisDisplay.Changed += OnPresentationChanged;
        _marinerSettings.Changed += OnMarinerChanged;
    }

    public void Dispose()
    {
        _settings.PaletteChanged -= OnPaletteChanged;
        _settings.DisplayScaleChanged -= OnPresentationChanged;
        _ecdisDisplay.Changed -= OnPresentationChanged;
        _marinerSettings.Changed -= OnMarinerChanged;
    }

    private void OnPaletteChanged(PaletteType _) => ApplyCurrent();

    private void OnMarinerChanged(MarinerSettings _) => ApplyCurrent();

    private void OnPresentationChanged() => ApplyCurrent();

    private void ApplyCurrent()
    {
        var presentation = _projection.CreateSnapshot();
        _ = ApplyCurrentAsync(presentation);
    }

    private async Task ApplyCurrentAsync(MapPresentationState presentation)
    {
        try
        {
            await _controller.SetPresentationAsync(presentation).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to apply map presentation:\n{ex}");
        }
    }
}
