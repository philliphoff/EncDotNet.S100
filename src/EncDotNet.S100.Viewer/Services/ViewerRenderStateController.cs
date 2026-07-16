using Avalonia.Threading;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IRenderStateController"/> implementation — drives
/// <see cref="SettingsViewModel.SelectedPalette"/> and
/// <see cref="EcdisDisplayState.SetCategory"/> through the Avalonia UI
/// dispatcher.
/// </summary>
/// <remarks>
/// Setting <see cref="SettingsViewModel.SelectedPalette"/> raises
/// <c>PropertyChanged</c> and <c>PaletteChanged</c>; bound view-models
/// expect those notifications on the UI thread. We therefore marshal
/// every setter through <see cref="Dispatcher.UIThread"/> regardless
/// of caller thread (a no-op when already on the UI thread).
/// </remarks>
internal sealed class ViewerRenderStateController : IRenderStateController
{
    private readonly SettingsViewModel _settings;
    private readonly EcdisDisplayState _ecdis;

    public ViewerRenderStateController(SettingsViewModel settings, EcdisDisplayState ecdis)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ecdis);
        _settings = settings;
        _ecdis = ecdis;
    }

    public PaletteType CurrentPalette => _settings.SelectedPalette;

    public EcdisDisplayCategory CurrentDisplayCategory => _ecdis.Category;

    public RenderSubsystemKind CurrentRenderSubsystem => _settings.SelectedRenderSubsystem;

    public bool RenderSubsystemPinned => RenderingOptimizations.RenderSubsystemEnvExplicit;

    public async Task SetPaletteAsync(PaletteType palette, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_settings.SelectedPalette == palette) return;
        await Dispatcher.UIThread.InvokeAsync(() => _settings.SelectedPalette = palette);
    }

    public async Task SetDisplayCategoryAsync(EcdisDisplayCategory category, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_ecdis.Category == category) return;
        await Dispatcher.UIThread.InvokeAsync(() => _ecdis.SetCategory(category));
    }

    public async Task SetRenderSubsystemAsync(RenderSubsystemKind subsystem, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (RenderingOptimizations.RenderSubsystemEnvExplicit)
        {
            throw new InvalidOperationException(
                "The render subsystem is pinned by the S100_RENDER_SUBSYSTEM environment variable and cannot be switched at runtime.");
        }

        if (_settings.SelectedRenderSubsystem == subsystem) return;
        await Dispatcher.UIThread.InvokeAsync(() => _settings.SelectedRenderSubsystem = subsystem);
    }

    public string? GetDisplayMode(string spec) => _ecdis.GetDisplayMode(spec);

    public async Task SetDisplayModeAsync(string spec, string? modeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        ct.ThrowIfCancellationRequested();
        if (string.Equals(_ecdis.GetDisplayMode(spec), modeId, StringComparison.Ordinal)) return;
        await Dispatcher.UIThread.InvokeAsync(() => _ecdis.SetDisplayMode(spec, modeId));
    }
}
