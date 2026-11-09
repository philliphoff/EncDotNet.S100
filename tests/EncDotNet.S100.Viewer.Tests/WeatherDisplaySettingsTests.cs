using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.McpTools;
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
        using var panel = new EcdisDisplayPanelViewModel(new EcdisDisplayState(), new PortrayalCatalogueManager(), datasets, labelOverrides: new EcdisLabelOverrideProvider());
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
    public void Isobars_and_currents_are_on_and_the_wind_speed_fill_wave_arrows_and_both_isotherms_off_by_default()
    {
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), new DatasetsViewModel(new FakeDatasetLoaderService()), labelOverrides: new EcdisLabelOverrideProvider());

        Assert.True(panel.IsIsobarsVisible);
        Assert.False(panel.IsWindSpeedFillVisible);
        Assert.False(panel.IsWaveArrowsVisible);
        Assert.False(panel.IsIsothermsVisible);
        Assert.True(panel.IsCurrentArrowsVisible);
        Assert.False(panel.IsAirTemperatureVisible);
        Assert.False(panel.IsPrecipitationVisible);
        Assert.False(panel.IsThunderstormRiskVisible);
        Assert.False(panel.IsCloudCoverVisible);
        // Unset switches follow the styles' defaults, so nothing is in the snapshot yet.
        Assert.Empty(state.Snapshot().LayerVisibility);
    }

    [Fact]
    public void A_switch_reaches_the_display_settings_and_re_renders()
    {
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), new DatasetsViewModel(new FakeDatasetLoaderService()), labelOverrides: new EcdisLabelOverrideProvider());
        var changed = 0;
        state.Changed += () => changed++;
        var notified = new List<string?>();
        panel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        panel.IsIsobarsVisible = false;
        panel.IsWindSpeedFillVisible = true;
        panel.IsWindSpeedFillVisible = true; // no-op
        panel.IsIsothermsVisible = true;
        panel.IsCurrentArrowsVisible = false;
        panel.IsAirTemperatureVisible = true;

        // Changed is what re-renders every dataset (ViewerPresentationCoordinator).
        Assert.Equal(5, changed);
        Assert.False(panel.IsIsobarsVisible);
        Assert.True(panel.IsWindSpeedFillVisible);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsIsobarsVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsIsothermsVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsCurrentArrowsVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsAirTemperatureVisible), notified);

        var display = state.Snapshot();
        Assert.False(display.LayerVisibility[PressureIsobarStyle.Key]);
        Assert.True(display.LayerVisibility[WindSpeedStyle.Key]);
        Assert.False(new PressureIsobarStyle().IsDrawnWith(display));
        Assert.True(new WindSpeedStyle().IsDrawnWith(display));
        Assert.False(new WaveDirectionStyle().IsDrawnWith(display));
        Assert.True(new SeaTemperatureIsothermStyle().IsDrawnWith(display));
        Assert.True(new SeaTemperatureStyle().IsDrawnWith(display));
        Assert.False(new CurrentArrowStyle().IsDrawnWith(display));
        Assert.True(new AirTemperatureIsothermStyle().IsDrawnWith(display));
        Assert.True(new FreezingSprayStyle().IsDrawnWith(display));
    }

    [Fact]
    public void The_precipitation_thunderstorm_and_cloud_switches_turn_their_layers_on()
    {
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), new DatasetsViewModel(new FakeDatasetLoaderService()), labelOverrides: new EcdisLabelOverrideProvider());
        var notified = new List<string?>();
        panel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        Assert.False(new PrecipitationStyle().IsDrawnWith(state.Snapshot()));

        panel.IsPrecipitationVisible = true;
        panel.IsThunderstormRiskVisible = true;
        panel.IsCloudCoverVisible = true;

        var display = state.Snapshot();
        Assert.True(new PrecipitationStyle().IsDrawnWith(display));
        Assert.True(new ThunderstormRiskStyle().IsDrawnWith(display));
        Assert.True(new CloudCoverStyle().IsDrawnWith(display));
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsPrecipitationVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsThunderstormRiskVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsCloudCoverVisible), notified);

        panel.IsThunderstormRiskVisible = false;
        Assert.False(new ThunderstormRiskStyle().IsDrawnWith(state.Snapshot()));
    }

    [Fact]
    public async Task The_precipitation_thunderstorm_and_cloud_switches_are_read_and_set_through_MCP()
    {
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), new DatasetsViewModel(new FakeDatasetLoaderService()), labelOverrides: new EcdisLabelOverrideProvider());
        var tools = new WeatherDisplayTools(panel, new ImmediateInvoker());

        var read = Assert.IsType<ToolResult<WeatherDisplayDto>.OkResult>(await tools.GetAsync(TestContext.Current.CancellationToken)).Value;
        Assert.False(read.Precipitation);
        Assert.False(read.ThunderstormRisk);
        Assert.False(read.CloudCover);

        var set = Assert.IsType<ToolResult<WeatherDisplayDto>.OkResult>(
            await tools.SetAsync(precipitation: true, cloudCover: true, cancellationToken: TestContext.Current.CancellationToken)).Value;
        Assert.True(set.Precipitation);
        Assert.False(set.ThunderstormRisk);
        Assert.True(set.CloudCover);
        Assert.True(state.GetLayerVisible(PrecipitationStyle.Key));
        Assert.Null(state.GetLayerVisible(ThunderstormRiskStyle.Key));
    }

    [AvaloniaFact]
    public void The_precipitation_thunderstorm_and_cloud_switches_have_automation_ids_and_names()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/gfs.grib2", WeatherProducts.Grib2.Spec);
        var state = new EcdisDisplayState();
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), datasets, labelOverrides: new EcdisLabelOverrideProvider());
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 1100);

        var precipitation = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.Precipitation");
        var cape = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.ThunderstormRisk");
        var cloud = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.CloudCover");

        Assert.Equal("Precipitation", AutomationProperties.GetName(precipitation));
        Assert.Equal("Thunderstorm risk", AutomationProperties.GetName(cape));
        Assert.Equal("Cloud cover", AutomationProperties.GetName(cloud));
        Assert.Equal(Strings.Tooltip_EcdisPanel_Weather_ThunderstormRisk, AutomationProperties.GetHelpText(cape));
        Assert.False(precipitation.IsChecked);
        Assert.False(cape.IsChecked);
        Assert.False(cloud.IsChecked);

        host.Click(cape);
        host.Settle();

        Assert.True(state.GetLayerVisible(ThunderstormRiskStyle.Key));
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
        using var panel = new EcdisDisplayPanelViewModel(state, new PortrayalCatalogueManager(), datasets, labelOverrides: new EcdisLabelOverrideProvider());
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
        using var panel = new EcdisDisplayPanelViewModel(new EcdisDisplayState(), new PortrayalCatalogueManager(), datasets, labelOverrides: new EcdisLabelOverrideProvider());
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 900);

        Assert.False(host.IsShown("EcdisDisplayPanel.Weather.Isobars"));
    }

    private sealed class ImmediateInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
