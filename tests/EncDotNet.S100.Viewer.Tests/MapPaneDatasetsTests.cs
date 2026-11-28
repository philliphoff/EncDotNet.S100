using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Core;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A map pane's header count and checklist (split map step 2 §2.6, D18,
/// #1770): "n of m datasets" for the pane's own map, a checklist with one
/// tri-state row per exchange set and then the loose datasets (Q5), and
/// <b>Open Datasets</b>.
/// </summary>
public sealed class MapPaneDatasetsTests
{
    private static readonly MapViewId Map1 = new("map-1");
    private static readonly MapViewId Map2 = new("map-2");

    private sealed class Host(MapViews views) : IMapPaneHost
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

    private sealed class StubAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public void Dispose() { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MapDatasetVisibility _visibility;

        public Fixture()
        {
            var views = new MapViews(new(), new(), new(), new DemandPlannerSet());
            _visibility = new MapDatasetVisibility(views, Datasets);
            Split = new MapSplitViewModel(views, new Host(views), () => { });
            Split.Restore(saved: null, restoreViews: true);
            Split.OpenSplit();
            Left = Checklist(Split.Panes[0]);
            Right = Checklist(Split.Panes[1]);
        }

        public DatasetsViewModel Datasets { get; } = new(new FakeDatasetLoaderService());

        public MapSplitViewModel Split { get; }

        public MapPaneDatasetsViewModel Left { get; }

        public MapPaneDatasetsViewModel Right { get; }

        public int OpenDatasetsCalls { get; private set; }

        public HashSet<DatasetEntry> EveryMap { get; } = [];

        public (ExchangeSetHeader Header, DatasetEntry A, DatasetEntry B) AddExchangeSet()
        {
            var source = new StubAssetSource();
            var header = Datasets.RegisterExchangeSetHeader(source, "/charts/ENC_ROOT", null, null, 2, _ => { });
            var a = Datasets.AddFromExchangeSet(source, "ENC_ROOT/US5WA12M.000", "S-57");
            var b = Datasets.AddFromExchangeSet(source, "ENC_ROOT/US4WA11M.000", "S-57");
            return (header, a, b);
        }

        public ViewHost Show()
        {
            var left = new MapPane();
            var right = new MapPane();
            left.Bind(Split.Panes[0], Left);
            right.Bind(Split.Panes[1], Right);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            return ViewHost.Show(grid, width: 1000, height: 600);
        }

        public void Dispose()
        {
            Left.Dispose();
            Right.Dispose();
            _visibility.Dispose();
        }

        private MapPaneDatasetsViewModel Checklist(MapPaneViewModel pane) => new(
            pane,
            Datasets,
            EveryMap.Contains,
            () => OpenDatasetsCalls++,
            action => action());
    }

    private static string CountText(ViewHost host, string map) =>
        AutomationProperties.GetName(host.Find<Button>($"MapPane.{map}.Datasets")) ?? string.Empty;

    private static CheckBox Row(ViewHost host, string label) =>
        host.Find<CheckBox>(c => AutomationProperties.GetAutomationId(c) == "MapPane.Datasets.Row"
            && AutomationProperties.GetName(c) == label);

    // ── Count ─────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Each_header_counts_the_datasets_its_own_map_shows()
    {
        using var f = new Fixture();
        var a = f.Datasets.Add("/tmp/US5WA12M.000", "S-57");
        f.Datasets.Add("/tmp/101US5WA12M.000", "S-101");
        using var host = f.Show();

        Assert.Equal("2 of 2 datasets", CountText(host, "map-1"));
        Assert.Equal("2 of 2 datasets", CountText(host, "map-2"));

        // A toggle in one pane moves only that pane's count.
        a.SetVisibleIn(Map2, false);
        host.Window.UpdateLayout();
        Assert.Equal("2 of 2 datasets", CountText(host, "map-1"));
        Assert.Equal("1 of 2 datasets", CountText(host, "map-2"));

        // The Datasets list's eye sets every map.
        a.ToggleVisibilityCommand.Execute(null);
        host.Window.UpdateLayout();
        Assert.Equal("1 of 2 datasets", CountText(host, "map-1"));
        Assert.Equal("1 of 2 datasets", CountText(host, "map-2"));

        // A load adds to both, shown in every pane (Q1).
        f.Datasets.Add("/tmp/US4WA11M.000", "S-57");
        host.Window.UpdateLayout();
        Assert.Equal("2 of 3 datasets", CountText(host, "map-1"));
        Assert.Equal("2 of 3 datasets", CountText(host, "map-2"));
    }

    // ── Checklist ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Clicking_the_count_opens_the_panes_checklist_with_exchange_sets_first()
    {
        using var f = new Fixture();
        var loose = f.Datasets.Add("/tmp/101US5WA12M.000", "S-101");
        var (header, _, _) = f.AddExchangeSet();
        using var host = f.Show();

        host.Click(host.Find<Button>("MapPane.map-2.Datasets"));
        host.Settle();

        Assert.True(f.Right.IsOpen);
        Assert.False(f.Left.IsOpen);
        var rows = host.FindAll<CheckBox>("MapPane.Datasets.Row");
        Assert.Equal(
            [header.DisplayName, loose.DisplayName],
            rows.Select(r => AutomationProperties.GetName(r)));
        Assert.All(rows, r => Assert.True(r.IsChecked));
        Assert.True(host.IsShown("MapPane.map-2.Datasets.Open"));
    }

    [AvaloniaFact]
    public void Toggling_a_row_sets_that_panes_visibility_only()
    {
        using var f = new Fixture();
        var a = f.Datasets.Add("/tmp/US5WA12M.000", "S-57");
        var b = f.Datasets.Add("/tmp/101US5WA12M.000", "S-101");
        using var host = f.Show();
        host.Click(host.Find<Button>("MapPane.map-2.Datasets"));
        host.Settle();

        host.Click(Row(host, a.DisplayName));
        host.Window.UpdateLayout();

        Assert.False(a.IsVisibleIn(Map2));
        Assert.True(a.IsVisibleIn(Map1));
        Assert.True(b.IsVisibleIn(Map2));
        Assert.False(Row(host, a.DisplayName).IsChecked);
        Assert.Equal("1 of 2 datasets", CountText(host, "map-2"));
        Assert.Equal("2 of 2 datasets", CountText(host, "map-1"));

        host.Click(Row(host, a.DisplayName));
        host.Window.UpdateLayout();

        Assert.True(a.IsVisibleIn(Map2));
        Assert.True(Row(host, a.DisplayName).IsChecked);
    }

    [AvaloniaFact]
    public void An_exchange_set_row_is_indeterminate_when_the_pane_shows_some_of_its_cells()
    {
        using var f = new Fixture();
        var (header, a, b) = f.AddExchangeSet();
        using var host = f.Show();
        host.Click(host.Find<Button>("MapPane.map-2.Datasets"));
        host.Settle();

        // A toggle made elsewhere (a Datasets row chip) shows in the open list.
        a.SetVisibleIn(Map2, false);
        host.Window.UpdateLayout();
        var row = Row(host, header.DisplayName);
        Assert.Null(row.IsChecked);
        Assert.Equal("1 of 2 shown", AutomationProperties.GetHelpText(row));

        // Indeterminate shows every cell in this pane.
        host.Click(row);
        host.Window.UpdateLayout();
        Assert.True(Row(host, header.DisplayName).IsChecked);
        Assert.True(a.IsVisibleIn(Map2));
        Assert.True(b.IsVisibleIn(Map2));

        // Checked hides every cell in this pane, and only this one.
        host.Click(Row(host, header.DisplayName));
        host.Window.UpdateLayout();
        Assert.False(Row(host, header.DisplayName).IsChecked);
        Assert.False(a.IsVisibleIn(Map2));
        Assert.False(b.IsVisibleIn(Map2));
        Assert.True(a.IsVisibleIn(Map1));
        Assert.True(b.IsVisibleIn(Map1));
        Assert.Equal("0 of 2 datasets", CountText(host, "map-2"));
    }

    [AvaloniaFact]
    public void Open_Datasets_opens_the_tab_and_closes_the_checklist()
    {
        using var f = new Fixture();
        f.Datasets.Add("/tmp/US5WA12M.000", "S-57");
        using var host = f.Show();
        host.Click(host.Find<Button>("MapPane.map-1.Datasets"));
        host.Settle();

        host.Click(host.Find<Button>("MapPane.map-1.Datasets.Open"));
        host.Settle();

        Assert.Equal(1, f.OpenDatasetsCalls);
        Assert.False(f.Left.IsOpen);
        Assert.False(host.IsShown("MapPane.map-1.Datasets.Open"));
    }

    // ── View-model ────────────────────────────────────────────────────

    [Fact]
    public void A_row_drawn_in_every_map_toggles_every_map()
    {
        // A received route keeps one eye (Q4): its row shows or hides it everywhere.
        using var f = new Fixture();
        var route = f.Datasets.Add("/tmp/route.gml", "S-421");
        f.EveryMap.Add(route);
        f.Right.IsOpen = true;

        f.Right.Rows.Single().IsChecked = false;

        Assert.False(route.IsVisibleIn(Map1));
        Assert.False(route.IsVisibleIn(Map2));
        Assert.Equal(0, f.Left.Shown);
    }

    [Fact]
    public void The_rows_follow_loads_and_removals_while_open()
    {
        using var f = new Fixture();
        var a = f.Datasets.Add("/tmp/US5WA12M.000", "S-57");
        f.Left.IsOpen = true;
        Assert.Single(f.Left.Rows);

        var (header, _, _) = f.AddExchangeSet();
        Assert.Equal([header.DisplayName, a.DisplayName], f.Left.Rows.Select(r => r.Label));
        Assert.Equal(3, f.Left.Total);

        f.Datasets.RemoveCommand.Execute(a);
        Assert.Equal([header.DisplayName], f.Left.Rows.Select(r => r.Label));
        Assert.Equal(2, f.Left.Total);
    }
}
