using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="MapSplitViewModel"/> (split map, #1732; design note D1–D5,
/// D17, D19): opening and closing panes, the active pane, restoring the split
/// and folding panes that don't fit.
/// </summary>
public sealed class MapSplitViewModelTests
{
    private static readonly MapPaneViewport Shilshole = new(-13_617_000, 6_050_000, 2.4);
    private static readonly MapPaneViewport Sound = new(-13_620_000, 6_040_000, 66);

    private sealed class Fixture
    {
        public Fixture()
        {
            Views = new MapViews(
                new MapCapabilityAccessor<IMapViewportController>(),
                new MapCapabilityAccessor<IMapCoordinateConverter>(),
                new MapCapabilityAccessor<IImageRenderer>(),
                new DemandPlannerSet());
            Host = new FakePaneHost(Views);
            Split = new MapSplitViewModel(Views, Host, () => SaveRequests++);
        }

        public MapViews Views { get; }

        public FakePaneHost Host { get; }

        public MapSplitViewModel Split { get; }

        public int SaveRequests { get; set; }

        public string[] Ids => [.. Split.Panes.Select(p => p.Id.Value)];

        public MapPaneViewModel Pane(string id) => Split.Panes.Single(p => p.Id.Value == id);
    }

    private sealed class FakePaneHost(MapViews views) : IMapPaneHost
    {
        public Dictionary<MapViewId, MapPaneViewport?> Viewports { get; } = [];

        public Dictionary<MapViewId, MapPaneViewport?> OpenedAt { get; } = [];

        public List<MapViewId> Closed { get; } = [];

        public IMapViewCapabilities OpenPane(MapViewId id, MapPaneViewport? viewport)
        {
            var view = new MapViewsTests.FakeMapView(id.Value);
            OpenedAt[id] = viewport;
            Viewports[id] = viewport;
            views.Add(view);
            return view;
        }

        public void ClosePane(IMapViewCapabilities view)
        {
            Closed.Add(view.Id);
            views.Remove(view);
        }

        public MapPaneViewport? GetViewport(IMapViewCapabilities view) => Viewports.GetValueOrDefault(view.Id);
    }

    private static Fixture Split()
    {
        var f = new Fixture();
        f.Split.Restore(saved: null, restoreViews: true);
        f.Host.Viewports[new MapViewId("map-1")] = Shilshole;
        f.Split.OpenSplit();
        return f;
    }

    [Fact]
    public void One_map_opens_by_default_with_no_header()
    {
        var f = new Fixture();

        f.Split.Restore(saved: null, restoreViews: true);

        Assert.Equal(["map-1"], f.Ids);
        Assert.False(f.Split.IsSplit);
        var pane = f.Pane("map-1");
        Assert.True(pane.IsActive);
        Assert.False(pane.IsHeaderVisible);
        Assert.Same(pane.View, f.Views.Active);
    }

    [Fact]
    public void Opening_the_split_adds_an_active_pane_at_the_active_view()
    {
        // D2: the same centre and scale; the new pane is the one the user works in.
        var f = Split();

        Assert.Equal(["map-1", "map-2"], f.Ids);
        Assert.True(f.Split.IsSplit);
        Assert.Equal(Shilshole, f.Host.OpenedAt[new MapViewId("map-2")]);
        Assert.True(f.Pane("map-2").IsActive);
        Assert.False(f.Pane("map-1").IsActive);
        Assert.Same(f.Pane("map-2").View, f.Views.Active);
        // D9, D19: headers while split, numbered in reading order.
        Assert.All(f.Split.Panes, p => Assert.True(p.IsHeaderVisible));
        Assert.Equal(["Map 1", "Map 2"], f.Split.Panes.Select(p => p.Label));
        Assert.True(f.SaveRequests > 0);
    }

    [Fact]
    public void Activating_a_pane_moves_the_active_map()
    {
        // D4, D5: one active pane, the one last used.
        var f = Split();

        f.Split.Activate(new MapViewId("map-1"));

        Assert.True(f.Pane("map-1").IsActive);
        Assert.False(f.Pane("map-2").IsActive);
        Assert.Same(f.Pane("map-1").View, f.Views.Active);
        Assert.Same(f.Pane("map-1"), f.Split.ActivePane);
    }

    [Fact]
    public void The_active_pane_follows_the_registry()
    {
        // A later MCP set_active_map goes through MapViews; the header follows.
        var f = Split();

        f.Views.Activate(f.Pane("map-1").View);

        Assert.True(f.Pane("map-1").IsActive);
        Assert.False(f.Pane("map-2").IsActive);
    }

    [Fact]
    public void Closing_any_pane_keeps_the_others()
    {
        // D3: close the first pane while the second is active.
        var f = Split();

        f.Pane("map-1").CloseCommand.Execute(null);

        Assert.Equal(["map-2"], f.Ids);
        Assert.Equal([new MapViewId("map-1")], f.Host.Closed);
        Assert.False(f.Split.IsSplit);
        var remaining = f.Pane("map-2");
        Assert.True(remaining.IsActive);
        Assert.Equal(1, remaining.Number);
        Assert.False(remaining.IsHeaderVisible);
    }

    [Fact]
    public void Closing_the_active_pane_makes_another_active()
    {
        var f = Split();

        f.Split.Close(f.Pane("map-2"));

        Assert.Equal(["map-1"], f.Ids);
        Assert.True(f.Pane("map-1").IsActive);
        Assert.Same(f.Pane("map-1").View, f.Views.Active);
    }

    [Fact]
    public void The_last_pane_cannot_be_closed()
    {
        var f = new Fixture();
        f.Split.Restore(saved: null, restoreViews: true);

        f.Split.Close(f.Pane("map-1"));

        Assert.Equal(["map-1"], f.Ids);
        Assert.Empty(f.Host.Closed);
    }

    [Fact]
    public void Turning_the_split_off_keeps_the_active_pane()
    {
        // D3: the user is looking at the detail in Map 2 and keeps it.
        var f = Split();
        Assert.True(f.Split.CloseOtherMapsCommand.CanExecute(null));

        f.Split.ToggleSplitCommand.Execute(null);

        Assert.Equal(["map-2"], f.Ids);
        Assert.False(f.Split.CloseOtherMapsCommand.CanExecute(null));
    }

    [Fact]
    public void A_new_pane_gets_an_id_not_in_use()
    {
        var f = Split();
        f.Split.Close(f.Pane("map-1"));

        f.Split.OpenSplit();

        Assert.Equal(["map-2", "map-3"], f.Ids);
    }

    [Fact]
    public void Swapping_renumbers_the_panes_in_reading_order()
    {
        var f = Split();

        f.Split.SwapMapsCommand.Execute(null);

        Assert.Equal(["map-2", "map-1"], f.Ids);
        Assert.Equal(1, f.Pane("map-2").Number);
        Assert.Equal(2, f.Pane("map-1").Number);
        Assert.True(f.Pane("map-2").IsActive);
    }

    [Fact]
    public void The_split_and_its_views_are_written_and_restored()
    {
        // D2: the split, the panes and each pane's view, keyed by map id.
        var f = Split();
        f.Host.Viewports[new MapViewId("map-2")] = Sound;
        f.Pane("map-1").Weight = 0.8;
        f.Split.Activate(new MapViewId("map-1"));
        var saved = new MapLayoutSettings();
        f.Split.WriteTo(saved);

        Assert.Equal("map-1", saved.ActiveMap);
        Assert.Equal(["map-1", "map-2"], saved.Panes.Select(p => p.Id));
        Assert.Equal(Sound.Resolution, saved.Panes[1].Resolution);

        var restored = new Fixture();
        restored.Split.Restore(saved, restoreViews: true);

        Assert.Equal(["map-1", "map-2"], restored.Ids);
        Assert.Equal(Shilshole, restored.Host.OpenedAt[new MapViewId("map-1")]);
        Assert.Equal(Sound, restored.Host.OpenedAt[new MapViewId("map-2")]);
        Assert.True(restored.Pane("map-1").IsActive);
        Assert.Equal(0.8, restored.Pane("map-1").Weight);
    }

    [Fact]
    public void A_view_set_on_the_command_line_wins_over_the_saved_views()
    {
        var saved = new MapLayoutSettings
        {
            ActiveMap = "map-2",
            Panes =
            [
                new() { Id = "map-1", CenterX = 1, CenterY = 2, Resolution = 3 },
                new() { Id = "map-2", CenterX = 4, CenterY = 5, Resolution = 6 },
            ],
        };
        var f = new Fixture();

        f.Split.Restore(saved, restoreViews: false);

        Assert.Equal(["map-1", "map-2"], f.Ids);
        Assert.All(f.Host.OpenedAt.Values, v => Assert.Null(v));
        Assert.True(f.Pane("map-2").IsActive);
    }

    [Fact]
    public void One_saved_pane_opens_at_the_default_view()
    {
        // Without a split the map starts as it always has.
        var saved = new MapLayoutSettings
        {
            Panes = [new() { Id = "map-4", CenterX = 1, CenterY = 2, Resolution = 3 }],
        };
        var f = new Fixture();

        f.Split.Restore(saved, restoreViews: true);

        Assert.Equal(["map-4"], f.Ids);
        Assert.Null(f.Host.OpenedAt[new MapViewId("map-4")]);
    }

    [Fact]
    public void Two_panes_need_644_px()
    {
        Assert.Equal(644, MapSplitViewModel.RequiredWidth(2));
    }

    [Fact]
    public void Panes_that_fit_do_not_fold()
    {
        // D17: the default 1100 × 700 window with the left dock open has a 732 px map area.
        var f = Split();

        f.Split.MapAreaWidth = 732;

        Assert.All(f.Split.Panes, p => Assert.False(p.IsFolded));
    }

    [Fact]
    public void A_map_area_too_narrow_folds_the_panes_that_are_not_active()
    {
        // D17 frame 7a: a 632 px map area folds Map 1.
        var f = Split();
        var layoutChanges = 0;
        f.Split.LayoutChanged += (_, _) => layoutChanges++;

        f.Split.MapAreaWidth = 632;

        Assert.True(f.Pane("map-1").IsFolded);
        Assert.False(f.Pane("map-1").IsHeaderVisible);
        Assert.False(f.Pane("map-2").IsFolded);
        Assert.True(layoutChanges > 0);
    }

    [Fact]
    public void Clicking_a_folded_strip_unfolds_it_and_folds_the_other()
    {
        var f = Split();
        f.Split.MapAreaWidth = 632;

        f.Pane("map-1").ActivateCommand.Execute(null);

        Assert.False(f.Pane("map-1").IsFolded);
        Assert.True(f.Pane("map-1").IsActive);
        Assert.True(f.Pane("map-2").IsFolded);
    }

    [Fact]
    public void Widening_the_map_area_unfolds_every_pane()
    {
        var f = Split();
        f.Split.MapAreaWidth = 632;

        f.Split.MapAreaWidth = 1052;

        Assert.All(f.Split.Panes, p => Assert.False(p.IsFolded));
    }

    [Fact]
    public void One_pane_never_folds()
    {
        var f = new Fixture();
        f.Split.Restore(saved: null, restoreViews: true);

        f.Split.MapAreaWidth = 200;

        Assert.False(f.Pane("map-1").IsFolded);
    }

    [Fact]
    public void Turning_the_split_off_unfolds_the_pane_left()
    {
        var f = Split();
        f.Split.MapAreaWidth = 632;
        f.Pane("map-1").ActivateCommand.Execute(null);

        f.Split.CloseSplit();

        Assert.Equal(["map-1"], f.Ids);
        Assert.False(f.Pane("map-1").IsFolded);
    }
}
