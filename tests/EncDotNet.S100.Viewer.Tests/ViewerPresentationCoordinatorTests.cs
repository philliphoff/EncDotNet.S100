using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class ViewerPresentationCoordinatorTests
{
    [Fact]
    public void ViewerChanges_ApplyExplicitPresentationSnapshots()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings);
        var ecdis = new EcdisDisplayState();
        var mariner = new StubMarinerSettingsProvider();
        var projection = new MapPresentationStateProjection(
            settingsViewModel, ecdis, mariner);
        var controller = new RecordingPresentationController();
        using var coordinator = new ViewerPresentationCoordinator(
            settingsViewModel, ecdis, mariner, projection, controller);

        settingsViewModel.SelectedPalette = PaletteType.Night;
        settingsViewModel.SymbolScale = 1.25;
        ecdis.SetDisplayMode("S-411", "Navigational");
        mariner.Set(MarinerSettings.Default with { FourShades = false });

        Assert.Equal(4, controller.Presentations.Count);
        var final = controller.Presentations[^1];
        Assert.Equal(PaletteType.Night, final.Palette);
        Assert.Equal(1.25, final.SymbolScale);
        Assert.Equal("Navigational", final.EcdisDisplay.ActiveDisplayModes["S-411"]);
        Assert.False(final.Mariner.FourShades);
    }

    [Fact]
    public void Dispose_StopsPresentationApplications()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings);
        var ecdis = new EcdisDisplayState();
        var mariner = new StubMarinerSettingsProvider();
        var projection = new MapPresentationStateProjection(
            settingsViewModel, ecdis, mariner);
        var controller = new RecordingPresentationController();
        var coordinator = new ViewerPresentationCoordinator(
            settingsViewModel, ecdis, mariner, projection, controller);

        coordinator.Dispose();
        settingsViewModel.SelectedPalette = PaletteType.Dusk;

        Assert.Empty(controller.Presentations);
    }

    private sealed class RecordingPresentationController : IMapPresentationController
    {
        public List<MapPresentationState> Presentations { get; } = [];

        public Task SetPresentationAsync(
            MapPresentationState presentation,
            CancellationToken cancellationToken = default)
        {
            Presentations.Add(presentation);
            return Task.CompletedTask;
        }
    }

    private sealed class StubMarinerSettingsProvider : IMarinerSettingsProvider
    {
        public MarinerSettings Current { get; private set; } = MarinerSettings.Default;

        public event Action<MarinerSettings>? Changed;

        public void Set(MarinerSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
        }
    }
}
