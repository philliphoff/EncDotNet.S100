using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public class MapPresentationStateProjectionTests
{
    [Fact]
    public void ViewerStateChanges_ReplaceRendererNeutralSnapshot()
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
        var original = projection.Current;
        var changed = 0;
        projection.Changed += () => changed++;

        settingsViewModel.SelectedPalette = PaletteType.Night;
        settingsViewModel.SymbolScale = 1.25;
        ecdis.SetCategory(EcdisDisplayCategory.All);
        mariner.Set(MarinerSettings.Default with { FourShades = false });

        Assert.NotSame(original, projection.Current);
        Assert.Equal(PaletteType.Night, projection.Current.Palette);
        Assert.Equal(1.25, projection.Current.SymbolScale);
        Assert.Equal(EcdisDisplayCategory.All, projection.Current.EcdisDisplay.Category);
        Assert.False(projection.Current.Mariner.FourShades);
        Assert.Equal(4, changed);
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
