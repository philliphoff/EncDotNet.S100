using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Display controls' Weather section (#1072): map-wide switches for the
/// optional weather layers, shown while a GRIB2 or NetCDF dataset is loaded,
/// carried to every weather dataset through the display settings and kept
/// between sessions with them.
/// </summary>
public sealed class WeatherDisplaySettingsTests
{
    [Fact]
    public void The_section_shows_only_while_a_weather_dataset_is_loaded()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        using var panel = new EcdisDisplayPanelViewModel(new EcdisDisplayState(), new PortrayalCatalogueManager(), datasets);
        Assert.False(panel.HasWeatherData);
        Assert.True(panel.IsEmpty);

        datasets.Add("/data/US5SEAFL.000", "S-57");
        Assert.False(panel.HasWeatherData);

        var grib = datasets.Add("/data/gfs.grib2", WeatherProducts.Grib2.Spec);
        Assert.True(panel.HasWeatherData);
        Assert.False(panel.IsEmpty);

        datasets.RemoveCommand.Execute(grib);
        Assert.False(panel.HasWeatherData);

        datasets.Add("/data/waves.nc", WeatherProducts.NetCdf.Spec);
        Assert.True(panel.HasWeatherData);
    }

    [Fact]
    public void Isobars_are_on_and_the_wind_speed_fill_and_wave_arrows_off_by_default()
    {
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), new DatasetsViewModel(new FakeDatasetLoaderService()));

        Assert.True(panel.IsIsobarsVisible);
        Assert.False(panel.IsWindSpeedFillVisible);
        Assert.False(panel.IsWaveArrowsVisible);
        // Unset switches follow the styles' defaults, so nothing is in the snapshot yet.
        Assert.Empty(state.Snapshot().LayerVisibility);
    }

    [Fact]
    public void A_switch_reaches_the_display_settings_and_re_renders()
    {
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), new DatasetsViewModel(new FakeDatasetLoaderService()));
        var changed = 0;
        state.Changed += () => changed++;
        var notified = new List<string?>();
        panel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        panel.IsIsobarsVisible = false;
        panel.IsWindSpeedFillVisible = true;
        panel.IsWindSpeedFillVisible = true; // no-op

        // Changed is what re-renders every dataset (ViewerPresentationCoordinator).
        Assert.Equal(2, changed);
        Assert.False(panel.IsIsobarsVisible);
        Assert.True(panel.IsWindSpeedFillVisible);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsIsobarsVisible), notified);

        var display = state.Snapshot();
        Assert.False(display.LayerVisibility[PressureIsobarStyle.Key]);
        Assert.True(display.LayerVisibility[WindSpeedStyle.Key]);
        Assert.False(new PressureIsobarStyle().IsDrawnWith(display));
        Assert.True(new WindSpeedStyle().IsDrawnWith(display));
        Assert.False(new WaveDirectionStyle().IsDrawnWith(display));
    }

    [Fact]
    public void The_switches_round_trip_through_the_settings_file()
    {
        var dir = Directory.CreateTempSubdirectory("weather-switches-");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var state = EcdisDisplayStatePersistence.Create(ViewerSettings.Load(path));
            state.SetLayerVisible(PressureIsobarStyle.Key, false);
            state.SetLayerVisible(WaveDirectionStyle.Key, true);

            var reloaded = ViewerSettings.Load(path);
            Assert.False(reloaded.EcdisLayerVisibility[PressureIsobarStyle.Key]);
            Assert.True(reloaded.EcdisLayerVisibility[WaveDirectionStyle.Key]);

            var restored = EcdisDisplayStatePersistence.Create(reloaded);
            Assert.False(restored.GetLayerVisible(PressureIsobarStyle.Key));
            Assert.True(restored.GetLayerVisible(WaveDirectionStyle.Key));
            Assert.Null(restored.GetLayerVisible(WindSpeedStyle.Key));
            Assert.False(restored.Snapshot().IsLayerVisible(PressureIsobarStyle.Key, defaultVisible: true));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_fresh_profile_has_no_switches_set()
    {
        var state = EcdisDisplayStatePersistence.Create(new ViewerSettings { IsReadOnly = true });

        Assert.Null(state.GetLayerVisible(PressureIsobarStyle.Key));
        Assert.Empty(state.Snapshot().LayerVisibility);
    }

    [AvaloniaFact]
    public void The_switches_have_automation_ids_and_names()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/gfs.grib2", WeatherProducts.Grib2.Spec);
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), datasets);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 900);

        var isobars = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.Isobars");
        var windSpeed = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.WindSpeedFill");
        var waveArrows = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.WaveArrows");

        Assert.True(isobars.IsEffectivelyVisible);
        Assert.Equal(Strings.EcdisPanel_Weather_Isobars, AutomationProperties.GetName(isobars));
        Assert.Equal(Strings.EcdisPanel_Weather_WindSpeedFill, AutomationProperties.GetName(windSpeed));
        Assert.Equal(Strings.EcdisPanel_Weather_WaveArrows, AutomationProperties.GetName(waveArrows));
        Assert.Equal(Strings.Tooltip_EcdisPanel_Weather_Isobars, AutomationProperties.GetHelpText(isobars));
        Assert.Equal("Isobars", Strings.EcdisPanel_Weather_Isobars);
        Assert.True(isobars.IsChecked);
        Assert.False(windSpeed.IsChecked);
        Assert.False(waveArrows.IsChecked);

        host.Click(windSpeed);
        host.Settle();

        Assert.True(state.GetLayerVisible(WindSpeedStyle.Key));
    }

    [AvaloniaFact]
    public void The_section_is_hidden_without_weather_data()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5SEAFL.000", "S-57");
        using var panel = new EcdisDisplayPanelViewModel(new EcdisDisplayState(), new PortrayalCatalogueManager(), datasets);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 900);

        Assert.False(host.IsShown("EcdisDisplayPanel.Weather.Isobars"));
    }
}
