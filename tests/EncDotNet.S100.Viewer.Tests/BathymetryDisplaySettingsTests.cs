using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Display controls' Bathymetry section: the S-102 relief shading switch,
/// shown while an S-102 dataset is loaded, off by default, carried to every
/// S-102 dataset through the display settings.
/// </summary>
public sealed class BathymetryDisplaySettingsTests
{
    [Fact]
    public void The_section_shows_only_while_an_S102_dataset_is_loaded()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        using var panel = Panel(new EcdisDisplayState(), datasets);
        Assert.False(panel.HasBathymetryData);
        Assert.True(panel.IsEmpty);

        datasets.Add("/data/US5SEAFL.000", "S-57");
        Assert.False(panel.HasBathymetryData);

        var s102 = datasets.Add("/data/102NL005_518N044E.H5", "S-102");
        Assert.True(panel.HasBathymetryData);
        Assert.False(panel.IsEmpty);

        datasets.RemoveCommand.Execute(s102);
        Assert.False(panel.HasBathymetryData);
    }

    [Fact]
    public void An_S102_dataset_alone_is_not_an_empty_panel()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        using var panel = Panel(new EcdisDisplayState(), datasets);
        var notified = new List<string?>();
        panel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        datasets.Add("/data/102DE00KG45.h5", "S-102");

        Assert.False(panel.IsEmpty);
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsEmpty), notified);
    }

    [Fact]
    public void Relief_shading_is_off_by_default_and_the_switch_reaches_the_display_settings()
    {
        var state = new EcdisDisplayState();
        using var panel = Panel(state, new DatasetsViewModel(new FakeDatasetLoaderService()));
        var changed = 0;
        state.Changed += () => changed++;
        var notified = new List<string?>();
        panel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.False(panel.IsReliefShadingVisible);
        Assert.False(S102Relief.IsOn(state.Snapshot()));
        Assert.Empty(state.Snapshot().LayerVisibility);

        panel.IsReliefShadingVisible = true;
        panel.IsReliefShadingVisible = true; // no-op

        Assert.Equal(1, changed);
        Assert.True(panel.IsReliefShadingVisible);
        Assert.True(S102Relief.IsOn(state.Snapshot()));
        Assert.Contains(nameof(EcdisDisplayPanelViewModel.IsReliefShadingVisible), notified);
    }

    [Fact]
    public async Task Relief_shading_is_read_and_set_through_MCP()
    {
        var state = new EcdisDisplayState();
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        using var panel = Panel(state, datasets);
        var tools = new BathymetryDisplayTools(panel, new ImmediateInvoker());

        var read = Assert.IsType<ToolResult<BathymetryDisplayDto>.OkResult>(await tools.GetAsync(TestContext.Current.CancellationToken)).Value;
        Assert.False(read.SectionShown);
        Assert.False(read.ReliefShading);

        datasets.Add("/data/102NL005_518N044E.H5", "S-102");
        var set = Assert.IsType<ToolResult<BathymetryDisplayDto>.OkResult>(
            await tools.SetAsync(reliefShading: true, cancellationToken: TestContext.Current.CancellationToken)).Value;
        Assert.True(set.SectionShown);
        Assert.True(set.ReliefShading);
        Assert.True(state.GetLayerVisible(S102Relief.LayerKey));

        var untouched = Assert.IsType<ToolResult<BathymetryDisplayDto>.OkResult>(
            await tools.SetAsync(cancellationToken: TestContext.Current.CancellationToken)).Value;
        Assert.True(untouched.ReliefShading);
    }

    [AvaloniaFact]
    public void The_relief_switch_has_an_automation_id_and_name()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/102NL005_518N044E.H5", "S-102");
        var state = new EcdisDisplayState();
        using var panel = Panel(state, datasets);
        using var host = ViewHost.Show(new EcdisDisplayPanelView { DataContext = panel }, width: 360, height: 800);

        var relief = host.Find<ToggleSwitch>("EcdisDisplayPanel.Bathymetry.ReliefShading");

        Assert.Equal("Relief shading", AutomationProperties.GetName(relief));
        Assert.Equal(Strings.Tooltip_EcdisPanel_Bathymetry_ReliefShading, AutomationProperties.GetHelpText(relief));
        Assert.False(relief.IsChecked);

        host.Click(relief);
        host.Settle();

        Assert.True(state.GetLayerVisible(S102Relief.LayerKey));
    }

    private static EcdisDisplayPanelViewModel Panel(EcdisDisplayState state, DatasetsViewModel datasets) =>
        new(state, new PortrayalCatalogueManager(), datasets, labelOverrides: new EcdisLabelOverrideProvider(), displayModeToolbar: null, displayCombinations: null, amlDisplay: null);

    private sealed class ImmediateInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
