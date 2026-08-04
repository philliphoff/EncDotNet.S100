using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class MapPresentationStateProjectionTests
{
    [Fact]
    public void CreateSnapshot_CapturesCurrentViewerState()
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

        settingsViewModel.SelectedPalette = PaletteType.Night;
        settingsViewModel.SymbolScale = 1.25;
        ecdis.SetCategory(EcdisDisplayCategory.All);
        mariner.Set(MarinerSettings.Default with { FourShades = false });
        var snapshot = projection.CreateSnapshot();

        Assert.Equal(PaletteType.Night, snapshot.Palette);
        Assert.Equal(1.25, snapshot.SymbolScale);
        Assert.Equal(EcdisDisplayCategory.All, snapshot.EcdisDisplay.Category);
        Assert.False(snapshot.Mariner.FourShades);
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
