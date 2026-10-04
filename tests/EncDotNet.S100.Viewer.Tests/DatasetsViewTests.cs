using Avalonia.Controls;
using Avalonia.Input;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Datasets panel, bound and laid out headlessly. The binding tests
/// cover what the MCP <c>select_dataset</c> tool relies on; the flow tests
/// drive the panel with pointer and keyboard input, as a user would, finding
/// controls by their <c>Datasets.*</c> automation ids.
/// </summary>
public sealed class DatasetsViewTests
{
    private static ViewHost Show(DatasetsViewModel datasets)
        => ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);

    [AvaloniaFact]
    public void Inspector_tabs_follow_the_view_model_both_ways()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");
        datasets.SelectDataset(cell);
        datasets.InspectorTab = DatasetInspectorTab.Validation;

        using var host = Show(datasets);
        var inspector = host.Find<TabControl>("Datasets.Inspector");
        Assert.Equal(2, inspector.SelectedIndex);

        datasets.InspectorTab = DatasetInspectorTab.Layers;
        host.Settle();
        Assert.Equal(1, inspector.SelectedIndex);

        ((TabItem)inspector.Items[0]!).IsSelected = true;
        Assert.Equal(DatasetInspectorTab.Dataset, datasets.InspectorTab);
    }

    [AvaloniaFact]
    public void Selecting_a_dataset_highlights_its_row_on_the_datasets_tab()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5OTHER.000", "S-57");
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");

        using var host = Show(datasets);
        datasets.SelectDataset(cell);
        host.Settle();

        Assert.Same(cell, host.Find<ListBox>("Datasets.List").SelectedItem);
        Assert.True(host.Find<TabControl>("Datasets.Inspector").IsVisible);
    }

    [AvaloniaFact]
    public void Clicking_a_row_selects_the_dataset_and_opens_its_inspector()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5OTHER.000", "S-57");
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");

        using var host = Show(datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        Assert.False(datasets.HasSelection);

        host.Click(Row(host, cell));

        Assert.Same(cell, datasets.SelectedDataset);
        Assert.True(host.Find<TabControl>("Datasets.Inspector").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Clicking_an_inspector_tab_switches_the_inspector()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");
        datasets.SelectDataset(cell);

        using var host = Show(datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        Assert.Equal(DatasetInspectorTab.Dataset, datasets.InspectorTab);

        host.Click(host.Find<TabItem>("Datasets.Inspector.LayersTab"));
        Assert.Equal(DatasetInspectorTab.Layers, datasets.InspectorTab);

        host.Click(host.Find<TabItem>("Datasets.Inspector.ValidationTab"));
        Assert.Equal(DatasetInspectorTab.Validation, datasets.InspectorTab);
    }

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection_through_the_list()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5OTHER.000", "S-57");
        datasets.Add("/data/US5SEAFL.000", "S-57");

        using var host = Show(datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        // The list is in render order, newest on top; work down from the top row.
        var (top, next) = (datasets.Entries[0], datasets.Entries[1]);
        host.Click(Row(host, top));
        Assert.Same(top, datasets.SelectedDataset);

        host.Press(PhysicalKey.ArrowDown);
        Assert.Same(next, datasets.SelectedDataset);

        host.Press(PhysicalKey.ArrowUp);
        Assert.Same(top, datasets.SelectedDataset);
    }

    [AvaloniaFact]
    public void A_rows_buttons_act_on_that_dataset_only()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var other = datasets.Add("/data/US5OTHER.000", "S-57");
        var cell = datasets.Add("/data/US5SEAFL.000", "S-57");

        using var host = Show(datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        Assert.Equal([cell, other], datasets.Entries);

        host.Click(host.Find<Button>("Datasets.Row.ToggleVisibility", Row(host, cell)));
        Assert.False(cell.IsVisible);
        Assert.True(other.IsVisible);

        host.Click(host.Find<Button>("Datasets.Row.MoveDown", Row(host, cell)));
        Assert.Equal([other, cell], datasets.Entries);

        host.Click(host.Find<Button>("Datasets.Row.Remove", Row(host, cell)));
        Assert.Equal([other], datasets.Entries);
    }

    private static ListBoxItem Row(ViewHost host, DatasetEntry entry)
        => host.Find<ListBoxItem>(item => ReferenceEquals(item.DataContext, entry));
}
