using EncDotNet.S100.Core;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Per-map dataset visibility (split map step 2 §2.1, #1766): the
/// <see cref="DatasetEntry"/> API, <see cref="MapDatasetVisibility"/> seeding a
/// new map from the active one, pruning a closed map and keeping the survivor's
/// set on unsplit, and the policies that act on every map.
/// </summary>
public sealed class MapDatasetVisibilityTests
{
    private static readonly MapViewId Map1 = new("map-1");
    private static readonly MapViewId Map2 = new("map-2");

    private sealed class Fixture : IDisposable
    {
        public MapViews Views { get; } = new(new(), new(), new(), new DemandPlannerSet());

        public DatasetsViewModel Datasets { get; } = new(new FakeDatasetLoaderService());

        public MapViewsTests.FakeMapView First { get; } = new("map-1");

        public MapDatasetVisibility Visibility { get; }

        public Fixture()
        {
            Visibility = new MapDatasetVisibility(Views, Datasets);
            Views.Add(First);
        }

        public MapViewsTests.FakeMapView Split()
        {
            var second = new MapViewsTests.FakeMapView("map-2");
            Views.Add(second);
            return second;
        }

        public void Dispose() => Visibility.Dispose();
    }

    private sealed class StubAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public void Dispose() { }
    }

    private static Task Immediate(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    // ── DatasetEntry ──────────────────────────────────────────────────

    [Fact]
    public void An_entry_is_shown_in_every_map_by_default()
    {
        var entry = new DatasetEntry("/tmp/a.000", "S-57");

        Assert.True(entry.IsVisibleIn(Map1));
        Assert.True(entry.IsVisibleIn(Map2));
        Assert.True(entry.IsVisibleAnywhere);
    }

    [Fact]
    public void SetVisibleIn_changes_one_map_and_names_it()
    {
        var entry = new DatasetEntry("/tmp/a.000", "S-57");
        var raised = new List<MapViewId?>();
        entry.VisibilityChanged += (_, map) => raised.Add(map);

        entry.SetVisibleIn(Map2, false);
        entry.SetVisibleIn(Map2, false);

        Assert.True(entry.IsVisibleIn(Map1));
        Assert.False(entry.IsVisibleIn(Map2));
        Assert.True(entry.IsVisibleAnywhere);
        Assert.Equal([Map2], raised);
    }

    [Fact]
    public void SetVisible_acts_on_every_map_including_later_ones()
    {
        var entry = new DatasetEntry("/tmp/a.000", "S-57");
        entry.SetVisibleIn(Map2, false);
        var raised = new List<MapViewId?>();
        entry.VisibilityChanged += (_, map) => raised.Add(map);

        entry.SetVisible(false);

        Assert.False(entry.IsVisibleIn(Map1));
        Assert.False(entry.IsVisibleIn(Map2));
        Assert.False(entry.IsVisibleIn(new MapViewId("map-3")));
        Assert.False(entry.IsVisibleAnywhere);
        Assert.Equal([null], raised);

        entry.SetVisibleIn(Map1, true);
        Assert.True(entry.IsVisibleIn(Map1));
        Assert.False(entry.IsVisibleIn(Map2));
        Assert.True(entry.IsVisibleAnywhere);

        entry.SetVisible(true);
        Assert.True(entry.IsVisibleIn(Map2));
    }

    [Fact]
    public void The_eye_toggles_every_map()
    {
        var entry = new DatasetEntry("/tmp/a.000", "S-57");
        entry.SetVisibleIn(Map2, false);

        entry.ToggleVisibilityCommand.Execute(null);

        Assert.False(entry.IsVisibleIn(Map1));
        Assert.False(entry.IsVisibleIn(Map2));

        entry.ToggleVisibilityCommand.Execute(null);

        Assert.True(entry.IsVisibleIn(Map1));
        Assert.True(entry.IsVisibleIn(Map2));
    }

    // ── MapDatasetVisibility ──────────────────────────────────────────

    [Fact]
    public void A_new_map_starts_from_the_active_map_before_ViewAdded()
    {
        using var f = new Fixture();
        var hidden = f.Datasets.Add("/tmp/hidden.000", "S-57");
        var shown = f.Datasets.Add("/tmp/shown.000", "S-57");
        hidden.SetVisibleIn(Map1, false);
        // The loader follows ViewAdded; by then the new map must be seeded,
        // whatever order the handlers were added in.
        bool? hiddenAtAdd = null;
        f.Views.ViewAdded += (_, view) => hiddenAtAdd = !hidden.IsVisibleIn(view.Id);

        f.Split();

        Assert.True(hiddenAtAdd);
        Assert.False(hidden.IsVisibleIn(Map2));
        Assert.True(shown.IsVisibleIn(Map2));
    }

    [Fact]
    public void A_new_map_seeds_from_the_active_map_not_the_first()
    {
        using var f = new Fixture();
        var entry = f.Datasets.Add("/tmp/a.000", "S-57");
        var second = f.Split();
        entry.SetVisibleIn(Map1, false);
        f.Views.Activate(second);

        f.Views.Add(new MapViewsTests.FakeMapView("map-3"));

        Assert.True(entry.IsVisibleIn(new MapViewId("map-3")));
    }

    [Fact]
    public void A_closed_map_is_pruned_from_every_entry()
    {
        using var f = new Fixture();
        var entry = f.Datasets.Add("/tmp/a.000", "S-57");
        var second = f.Split();
        entry.SetVisibleIn(Map2, false);

        f.Views.Remove(second);

        // map-2 is gone, so a map reopened with its id starts from the active map.
        Assert.True(entry.IsVisibleIn(Map2));
        f.Split();
        Assert.True(entry.IsVisibleIn(Map2));
    }

    [Fact]
    public void Closing_the_split_keeps_the_surviving_maps_set()
    {
        using var f = new Fixture();
        var a = f.Datasets.Add("/tmp/a.000", "S-57");
        var b = f.Datasets.Add("/tmp/b.000", "S-101");
        var second = f.Split();
        a.SetVisibleIn(Map1, false);
        b.SetVisibleIn(Map2, false);

        f.Views.Remove(second);

        Assert.False(a.IsVisibleIn(Map1));
        Assert.False(a.IsVisibleAnywhere);
        Assert.True(b.IsVisibleIn(Map1));
        Assert.True(b.IsVisibleAnywhere);
    }

    [Fact]
    public void Closing_the_first_map_keeps_the_second_maps_set()
    {
        using var f = new Fixture();
        var a = f.Datasets.Add("/tmp/a.000", "S-57");
        var b = f.Datasets.Add("/tmp/b.000", "S-101");
        f.Split();
        a.SetVisibleIn(Map1, false);
        b.SetVisibleIn(Map2, false);

        f.Views.Remove(f.First);

        Assert.True(a.IsVisibleIn(Map2));
        Assert.True(a.IsVisibleAnywhere);
        Assert.False(b.IsVisibleIn(Map2));
        Assert.False(b.IsVisibleAnywhere);
    }

    [Fact]
    public void Hiding_in_each_open_map_one_by_one_is_hidden_anywhere()
    {
        using var f = new Fixture();
        var entry = f.Datasets.Add("/tmp/a.000", "S-57");
        f.Split();

        entry.SetVisibleIn(Map1, false);
        Assert.True(entry.IsVisibleAnywhere);
        entry.SetVisibleIn(Map2, false);

        Assert.False(entry.IsVisibleAnywhere);
        Assert.False(entry.IsVisibleIn(Map1));
        Assert.False(entry.IsVisibleIn(Map2));
        Assert.Equal(0.5, entry.RowOpacity);
    }

    [Fact]
    public void A_disposed_service_stops_seeding()
    {
        var f = new Fixture();
        var entry = f.Datasets.Add("/tmp/a.000", "S-57");
        f.Split();
        entry.SetVisibleIn(Map1, false);
        f.Dispose();

        var third = new MapViewsTests.FakeMapView("map-3");
        f.Views.Add(third);

        Assert.True(entry.IsVisibleIn(third.Id));
    }

    // ── Policies that act on every map ────────────────────────────────

    [Fact]
    public void The_exchange_set_header_hides_and_shows_every_map()
    {
        using var f = new Fixture();
        var source = new StubAssetSource();
        var header = f.Datasets.RegisterExchangeSetHeader(source, "/a", null, null, 2, _ => { });
        var d1 = f.Datasets.AddFromExchangeSet(source, "a/d1.000", "S-57");
        var d2 = f.Datasets.AddFromExchangeSet(source, "a/d2.000", "S-57");
        f.Split();
        d1.SetVisibleIn(Map2, false);

        header.ToggleVisibilityCommand.Execute(null);

        foreach (var map in new[] { Map1, Map2 })
        {
            Assert.False(d1.IsVisibleIn(map));
            Assert.False(d2.IsVisibleIn(map));
        }

        header.ToggleVisibilityCommand.Execute(null);

        foreach (var map in new[] { Map1, Map2 })
        {
            Assert.True(d1.IsVisibleIn(map));
            Assert.True(d2.IsVisibleIn(map));
        }
    }

    [Fact]
    public void Show_all_and_hide_all_act_on_every_map()
    {
        using var f = new Fixture();
        var a = f.Datasets.Add("/tmp/a.000", "S-57");
        f.Split();
        a.SetVisibleIn(Map2, false);

        f.Datasets.HideAllCommand.Execute(null);
        Assert.False(a.IsVisibleIn(Map1));
        Assert.False(a.IsVisibleIn(Map2));

        f.Datasets.ShowAllCommand.Execute(null);
        Assert.True(a.IsVisibleIn(Map1));
        Assert.True(a.IsVisibleIn(Map2));
    }

    [Fact]
    public async Task Set_dataset_state_without_a_map_acts_on_every_map()
    {
        // Split map step 2, Q6.
        using var f = new Fixture();
        var entry = f.Datasets.Add("/tmp/a.000", "S-57");
        f.Split();
        entry.SetVisibleIn(Map2, false);
        var tool = new SetDatasetStateTool(new ViewerDatasetStateController(f.Datasets, Immediate));

        var result = await tool.InvokeAsync(
            new SetDatasetStateRequest(entry.DisplayName, true, null), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out _));
        Assert.True(entry.IsVisibleIn(Map1));
        Assert.True(entry.IsVisibleIn(Map2));

        await tool.InvokeAsync(
            new SetDatasetStateRequest(entry.DisplayName, false, null), TestContext.Current.CancellationToken);

        Assert.False(entry.IsVisibleIn(Map1));
        Assert.False(entry.IsVisibleIn(Map2));
    }
}
