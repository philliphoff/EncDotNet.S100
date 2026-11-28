using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The number chips on Datasets and Layer stack rows while the map is split
/// (split map step 2 §2.6, D18, #1769), driven through the real panels by their
/// automation ids: click toggles the dataset in that pane, Alt-click shows it
/// only there, and the row menu's <b>Show in</b> is the keyboard path.
/// </summary>
public sealed class DatasetsPaneChipsViewTests
{
    internal static readonly MapViewId Map1 = new("map-1");
    internal static readonly MapViewId Map2 = new("map-2");

    /// <summary>The real split, datasets and panes over fake maps, with the chips attached inline.</summary>
    internal sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Views = new MapViews(
                new MapCapabilityAccessor<IMapViewportController>(),
                new MapCapabilityAccessor<IMapCoordinateConverter>(),
                new MapCapabilityAccessor<IImageRenderer>(),
                new DemandPlannerSet());
            Split = new MapSplitViewModel(Views, new FakePaneHost(Views), () => { });
            Split.Restore(saved: null, restoreViews: true);
            Datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
            Visibility = new MapDatasetVisibility(Views, Datasets);
            Panes = new TimelinePanes(Split, Datasets);
            Datasets.AttachPanes(Panes, action => action());
        }

        public MapViews Views { get; }

        public MapSplitViewModel Split { get; }

        public DatasetsViewModel Datasets { get; }

        public MapDatasetVisibility Visibility { get; }

        public TimelinePanes Panes { get; }

        public void Dispose()
        {
            Panes.Dispose();
            Visibility.Dispose();
        }
    }

    private sealed class FakePaneHost(MapViews views) : IMapPaneHost
    {
        public IMapViewCapabilities OpenPane(MapViewId id, MapPaneViewport? viewport)
        {
            var view = new MapViewsTests.FakeMapView(id.Value);
            views.Add(view);
            return view;
        }

        public void ClosePane(IMapViewCapabilities view) => views.Remove(view);

        public MapPaneViewport? GetViewport(IMapViewCapabilities view) => null;
    }

    private static ViewHost Show(DatasetsViewModel datasets)
        => ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 900);

    private static ListBoxItem Row(ViewHost host, DatasetEntry entry)
        => host.Find<ListBoxItem>(item => ReferenceEquals(item.DataContext, entry));

    // The row's content, which carries the row menu.
    private static Border RowBody(ViewHost host, DatasetEntry entry)
        => host.Find<Border>(b => ReferenceEquals(b.DataContext, entry) && b.ContextMenu is not null);

    private static bool IsShown(ViewHost host, string id, Avalonia.Visual within)
        => host.FindAll<Control>(id).Any(c => c.IsEffectivelyVisible && within.IsVisualAncestorOf(c));

    private static PaneChip Chip(ViewHost host, string id, Avalonia.Visual row, string name)
        => host.FindAll<PaneChip>(id).Single(c => c.IsEffectivelyVisible
            && AutomationProperties.GetName(c) == name
            && row.IsVisualAncestorOf(c));

    private static bool[] Shown(DatasetEntry entry) => [entry.IsVisibleIn(Map1), entry.IsVisibleIn(Map2)];

    [AvaloniaFact]
    public void With_one_pane_the_row_keeps_its_eye()
    {
        using var f = new Fixture();
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        Assert.True(host.Find<Button>("Datasets.Row.ToggleVisibility", Row(host, cell)).IsEffectivelyVisible);
        Assert.DoesNotContain(host.FindAll<PaneChip>("Datasets.Row.Chip"), c => c.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Clicking_a_chip_toggles_the_dataset_in_that_pane_and_alt_click_shows_it_only_there()
    {
        using var f = new Fixture();
        var other = f.Datasets.Add("/data/US5OTHER.000", "S-57");
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");
        f.Split.OpenSplit();

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        var row = Row(host, cell);
        Assert.False(IsShown(host, "Datasets.Row.ToggleVisibility", row));
        var two = Chip(host, "Datasets.Row.Chip", row, "Map 2");
        Assert.True(two.IsChecked);

        host.Click(two);

        Assert.Equal([true, false], Shown(cell));
        Assert.Equal([true, true], Shown(other));
        Assert.False(two.IsChecked);
        Assert.True(Chip(host, "Datasets.Row.Chip", row, "Map 1").IsChecked);

        host.Click(Chip(host, "Datasets.Row.Chip", row, "Map 2"), RawInputModifiers.Alt);

        Assert.Equal([false, true], Shown(cell));
        Assert.False(Chip(host, "Datasets.Row.Chip", row, "Map 1").IsChecked);
        Assert.Equal([true, true], Shown(other));
    }

    [AvaloniaFact]
    public void The_row_menu_shows_the_dataset_in_a_pane_only_or_in_every_map()
    {
        using var f = new Fixture();
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");
        f.Split.OpenSplit();

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        var menu = host.RightClick(RowBody(host, cell));
        var showIn = menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "Datasets.RowMenu.ShowIn");
        Assert.True(showIn.IsVisible);
        var items = showIn.Items.OfType<MenuItem>().ToList();
        Assert.Equal(
            ["Map 1", "Map 2", "Only Map 1", "Only Map 2", "Every map"],
            items.Select(AutomationProperties.GetName));
        Assert.All(items.Take(2), i => Assert.True(i.IsChecked));
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetAutomationId(i))));

        Invoke(items.Single(i => AutomationProperties.GetName(i) == "Only Map 1"));
        Assert.Equal([true, false], Shown(cell));
        menu.Close();

        // The menu reads the state again each time it opens.
        menu = host.RightClick(RowBody(host, cell));
        showIn = menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "Datasets.RowMenu.ShowIn");
        items = [.. showIn.Items.OfType<MenuItem>()];
        Assert.False(items.Single(i => AutomationProperties.GetName(i) == "Map 2").IsChecked);

        Invoke(items.Single(i => AutomationProperties.GetName(i) == "Map 2"));
        Assert.Equal([true, true], Shown(cell));

        Invoke(items.Single(i => AutomationProperties.GetName(i) == "Only Map 2"));
        Invoke(items.Single(i => AutomationProperties.GetAutomationId(i) == "Datasets.RowMenu.ShowIn.Every"));
        Assert.Equal([true, true], Shown(cell));
        menu.Close();
    }

    [AvaloniaFact]
    public void With_one_pane_the_row_menu_has_no_show_in()
    {
        using var f = new Fixture();
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));

        var menu = host.RightClick(RowBody(host, cell));
        Assert.False(menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "Datasets.RowMenu.ShowIn").IsVisible);
        menu.Close();
    }

    [AvaloniaFact]
    public void A_received_route_keeps_one_eye_while_split()
    {
        using var f = new Fixture();
        var route = f.Datasets.Add("/data/route.gml", "S-421");
        f.Split.OpenSplit();
        route.HasOneEye = true;

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        var eye = host.Find<Button>("Datasets.Row.ToggleVisibility", Row(host, route));
        Assert.True(eye.IsEffectivelyVisible);

        host.Click(eye);

        Assert.Equal([false, false], Shown(route));
        Assert.Empty(route.PaneChips);
    }

    [AvaloniaFact]
    public void The_exchange_set_header_chip_is_half_filled_when_the_pane_shows_some_cells()
    {
        using var f = new Fixture();
        var source = new EmptyAssetSource();
        var a = new DatasetEntry("/data/ENC_ROOT/US5A.000", "S-57", source, "US5A.000", displayName: null);
        var b = new DatasetEntry("/data/ENC_ROOT/US5B.000", "S-57", source, "US5B.000", displayName: null);
        f.Datasets.Entries.Add(a);
        f.Datasets.Entries.Add(b);
        var header = f.Datasets.RegisterExchangeSetHeader(source, "/data/ENC_ROOT", "ACME", "2026-10-01", 2, _ => { });
        f.Split.OpenSplit();
        b.SetVisibleIn(Map2, false);

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.ExchangeSetsTab"));
        var headerRow = host.Find<TreeViewItem>(i => ReferenceEquals(i.DataContext, header));
        var two = Chip(host, "Datasets.Source.Chip", headerRow, "Map 2");
        Assert.Null(two.IsChecked);
        Assert.True(Chip(host, "Datasets.Source.Chip", headerRow, "Map 1").IsChecked);
        Assert.False(IsShown(host, "Datasets.Source.ToggleVisibility", headerRow));

        // A half-filled chip shows every cell there.
        host.Click(two);
        Assert.Equal([true, true], Shown(a));
        Assert.Equal([true, true], Shown(b));
        Assert.True(two.IsChecked);

        // A filled one hides them all there.
        host.Click(two);
        Assert.Equal([true, false], Shown(a));
        Assert.Equal([true, false], Shown(b));
        Assert.False(two.IsChecked);

        host.Click(Chip(host, "Datasets.Source.Chip", headerRow, "Map 2"), RawInputModifiers.Alt);
        Assert.Equal([false, true], Shown(a));
        Assert.Equal([false, true], Shown(b));
    }

    [AvaloniaFact]
    public void Layer_stack_rows_carry_the_dataset_chips()
    {
        using var f = new Fixture();
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");
        var loader = new LayerStackViewModelTests.ControllableLoader();
        loader.SetEntries(StackEntry(cell.Id.Value, S98DisplayPlane.BaseChartUnder, 10));
        var stack = new LayerStackViewModel(loader, dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: f.Datasets, legend: null);
        f.Split.OpenSplit();

        using var host = ViewHost.Show(new LayerStackView { DataContext = stack }, width: 420, height: 700);
        var row = host.Find<Grid>(g => g.DataContext is LayerStackEntryViewModel);
        var two = Chip(host, "LayerStack.Entry.Chip", row, "Map 2");

        host.Click(two);

        Assert.Equal([true, false], Shown(cell));
        Assert.False(two.IsChecked);
        // The S-98 Active toggle stays one global switch (D26).
        Assert.True(host.Find<CheckBox>("LayerStack.Entry.Active", row).IsChecked);
    }

    [AvaloniaFact]
    public void Ui_automation_toggles_a_chip_by_its_id()
    {
        // What the MCP ui_invoke tool does: the chip reads as a toggle button.
        using var f = new Fixture();
        var cell = f.Datasets.Add("/data/US5SEAFL.000", "S-57");
        f.Split.OpenSplit();

        using var host = Show(f.Datasets);
        host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
        var two = Chip(host, "Datasets.Row.Chip", Row(host, cell), "Map 2");
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(two);
        var toggle = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IToggleProvider>(peer.GetProvider<Avalonia.Automation.Provider.IToggleProvider>());
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.On, toggle.ToggleState);
        Assert.Equal("Map 2", peer.GetName());

        toggle.Toggle();

        Assert.Equal([true, false], Shown(cell));
        Assert.Equal(Avalonia.Automation.Provider.ToggleState.Off, toggle.ToggleState);
    }

    private static void Invoke(MenuItem item) => item.Command!.Execute(item.CommandParameter);

    private static LayerStackEntry StackEntry(string id, S98DisplayPlane plane, int priority)
        => new(new Mapsui.Layers.MemoryLayer(id), new SubLayerStackItem(new SyntheticStackPayload(id), plane, priority, id));

    private sealed class EmptyAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public void Dispose() { }
    }
}
