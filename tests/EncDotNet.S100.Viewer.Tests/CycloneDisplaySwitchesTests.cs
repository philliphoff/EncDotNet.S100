using Avalonia;
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
/// Issue #1298 (V26): the Weather section shows for tropical cyclones and
/// surface analyses too, and ends with the Wind radii, Past track and
/// Forecast times switches, shown while a cyclone is loaded, on by default,
/// kept in the display settings and reachable through MCP.
/// </summary>
public sealed class CycloneDisplaySwitchesTests
{
    private static EcdisDisplayPanelViewModel Panel(DatasetsViewModel datasets, EcdisDisplayState? state = null) =>
        new(state ?? new EcdisDisplayState(), new PortrayalCatalogueManager(), datasets, new EcdisLabelOverrideProvider(), displayModeToolbar: null, displayCombinations: null, amlDisplay: null);

    [Fact]
    public void The_section_shows_for_a_cyclone_or_a_surface_analysis_and_the_cyclone_switches_only_for_a_cyclone()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        using var panel = Panel(datasets);

        var fronts = datasets.Add("/data/codsus.txt", WeatherProducts.Codsus.Spec);
        Assert.True(panel.HasWeatherData);
        Assert.False(panel.HasTropicalCyclone);

        var storm = datasets.Add("/data/al092022/al092022_5day_020.zip", WeatherProducts.TropicalCyclone.Spec);
        Assert.True(panel.HasTropicalCyclone);

        datasets.RemoveCommand.Execute(fronts);
        Assert.True(panel.HasWeatherData);

        datasets.RemoveCommand.Execute(storm);
        Assert.False(panel.HasWeatherData);
        Assert.False(panel.HasTropicalCyclone);
    }

    [Fact]
    public void The_cyclone_switches_are_on_by_default_and_reach_the_display_settings()
    {
        var state = new EcdisDisplayState();
        using var panel = Panel(new DatasetsViewModel(new FakeDatasetLoaderService()), state);
        var changed = 0;
        state.Changed += () => changed++;
        var notified = new List<string?>();
        panel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.True(panel.IsWindRadiiVisible);
        Assert.True(panel.IsPastTrackVisible);
        Assert.True(panel.IsForecastTimesVisible);
        Assert.Empty(state.Snapshot().LayerVisibility);

        panel.IsWindRadiiVisible = false;
        panel.IsPastTrackVisible = false;
        panel.IsForecastTimesVisible = false;

        // Changed is what re-renders every dataset.
        Assert.Equal(3, changed);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsWindRadiiVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsPastTrackVisible), notified);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsForecastTimesVisible), notified);
        var display = state.Snapshot();
        Assert.All(TropicalCycloneStyle.SwitchKeys, key => Assert.False(TropicalCycloneStyle.IsShown(display, key)));
    }

    [Fact]
    public async Task The_switches_are_read_and_set_through_MCP()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/al092022/al092022_5day_020.zip", WeatherProducts.TropicalCyclone.Spec);
        var state = new EcdisDisplayState();
        using var panel = Panel(datasets, state);
        var tools = new WeatherDisplayTools(panel, new ImmediateInvoker());

        var read = Assert.IsType<ToolResult<WeatherDisplayDto>.OkResult>(await tools.GetAsync(TestContext.Current.CancellationToken)).Value;
        Assert.True(read.SectionShown);
        Assert.True(read.CycloneSwitchesShown);
        Assert.True(read.WindRadii);
        Assert.True(read.PastTrack);
        Assert.True(read.ForecastTimes);
        Assert.True(read.Isobars);
        Assert.False(read.WindSpeedFill);

        var set = Assert.IsType<ToolResult<WeatherDisplayDto>.OkResult>(
            await tools.SetAsync(windRadii: false, forecastTimes: false, windSpeedFill: true, cancellationToken: TestContext.Current.CancellationToken)).Value;
        Assert.False(set.WindRadii);
        Assert.True(set.PastTrack);
        Assert.False(set.ForecastTimes);
        Assert.True(set.WindSpeedFill);
        Assert.False(state.GetLayerVisible(TropicalCycloneStyle.WindRadiiKey));
        Assert.Null(state.GetLayerVisible(TropicalCycloneStyle.PastTrackKey));
    }

    [AvaloniaFact]
    public void The_cyclone_switches_end_the_section_with_automation_ids_while_a_cyclone_is_loaded()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/al092022/al092022_5day_020.zip", WeatherProducts.TropicalCyclone.Spec);
        var state = new EcdisDisplayState();
        using var panel = Panel(datasets, state);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 1000);

        var radii = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.WindRadii");
        var past = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.PastTrack");
        var times = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.ForecastTimes");

        Assert.True(host.IsShown("EcdisDisplayPanel.Weather.Isobars"));
        Assert.True(radii.IsEffectivelyVisible);
        Assert.Equal(Strings.EcdisPanel_Weather_WindRadii, AutomationProperties.GetName(radii));
        Assert.Equal(Strings.EcdisPanel_Weather_PastTrack, AutomationProperties.GetName(past));
        Assert.Equal(Strings.EcdisPanel_Weather_ForecastTimes, AutomationProperties.GetName(times));
        Assert.Equal(Strings.Tooltip_EcdisPanel_Weather_WindRadii, AutomationProperties.GetHelpText(radii));
        Assert.Equal("Wind radii", Strings.EcdisPanel_Weather_WindRadii);
        Assert.Equal("Past track", Strings.EcdisPanel_Weather_PastTrack);
        Assert.Equal("Forecast times", Strings.EcdisPanel_Weather_ForecastTimes);
        Assert.True(radii.IsChecked);
        Assert.True(past.IsChecked);
        Assert.True(times.IsChecked);

        // They follow the air temperature switch, at the end of the section.
        var air = host.Find<ToggleSwitch>("EcdisDisplayPanel.Weather.AirTemperature");
        Assert.True(air.TranslatePoint(default, host.Window)!.Value.Y < radii.TranslatePoint(default, host.Window)!.Value.Y);

        host.Click(past);
        host.Settle();
        Assert.False(state.GetLayerVisible(TropicalCycloneStyle.PastTrackKey));
    }

    [AvaloniaFact]
    public void Without_a_cyclone_the_cyclone_switches_are_hidden()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/codsus.txt", WeatherProducts.Codsus.Spec);
        using var panel = Panel(datasets);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 1000);

        Assert.True(host.IsShown("EcdisDisplayPanel.Weather.Isobars"));
        Assert.False(host.IsShown("EcdisDisplayPanel.Weather.WindRadii"));
        Assert.False(host.IsShown("EcdisDisplayPanel.Weather.PastTrack"));
        Assert.False(host.IsShown("EcdisDisplayPanel.Weather.ForecastTimes"));
    }

    private sealed class ImmediateInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
