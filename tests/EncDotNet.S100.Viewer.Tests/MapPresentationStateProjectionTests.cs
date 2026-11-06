using EncDotNet.S100.Core;
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
            settingsViewModel, ecdis, mariner, TimeProvider.System);

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

    [Fact]
    public void Chart_times_follow_the_Local_UTC_preference_in_the_machine_zone()
    {
        // Times drawn on the chart, such as a cyclone's forecast times
        // (#1296), are written in the zone the user reads times in.
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(
                Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json"),
        };
        var settingsViewModel = new SettingsViewModel(settings);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");
        var projection = new MapPresentationStateProjection(
            settingsViewModel, new EcdisDisplayState(), new StubMarinerSettingsProvider(), new ZoneClock(zone));

        settingsViewModel.SelectedTimeFormat = TimeFormat.Utc;
        Assert.Equal(TimeZoneInfo.Utc, projection.CreateSnapshot().DisplayTimeZone);

        settingsViewModel.SelectedTimeFormat = TimeFormat.Local;
        var snapshot = projection.CreateSnapshot();
        Assert.Equal(zone, snapshot.DisplayTimeZone);
        Assert.Equal(zone, snapshot.ApplyTo(new RenderContext(), new SpecRef("TC", default)).DisplayTimeZone);
    }

    private sealed class ZoneClock(TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;
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
