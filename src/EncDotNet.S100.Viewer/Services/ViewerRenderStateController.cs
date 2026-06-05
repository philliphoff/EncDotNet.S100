using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Datasets.Pipelines;
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
}
