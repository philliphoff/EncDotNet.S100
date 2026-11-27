using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Timeline lanes across map panes (split map step 2 §2.6, D20, #1771): a lane
/// for every time-varying dataset, the number chips of the panes that draw it,
/// and a lane the active pane doesn't draw dimmed in place.
/// </summary>
public sealed class TimelineSplitLanesTests
{
    private static readonly DateTime Run = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly MapViewId Map1 = new("map-1");
    private static readonly MapViewId Map2 = new("map-2");

    /// <summary>The real split, datasets and pane source over fake maps, and a Timeline over three S-111 layers.</summary>
    private sealed class Fixture : IDisposable
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
            foreach (var name in new[] { "both", "first", "second" })
                Datasets.Entries.Add(new DatasetEntry($"/tmp/{name}.h5", "S-111"));
            Panes = new TimelinePanes(Split, Datasets);

            var service = new GlobalTimeService();
            Timeline = new TimelineViewModel(service, null, new FakeTimeProvider(new DateTimeOffset(Run.AddHours(5))), action => action(), refresher: null, progress: null, scope: null, library: null);
            var samples = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
            service.ApplySnapshot(new MapsuiMapTimeSnapshot
            {
                Minimum = samples[0],
                Maximum = samples[^1],
                Current = samples[0],
                Samples = samples,
                CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
                Datasets = [.. Datasets.Entries.Select(e => new MapsuiMapTimedDataset($"111US00_{e.Id.Value}", samples[0], samples[^1])
                {
                    DatasetId = e.Id.Value,
                    ProductSpec = "S-111",
                    Samples = samples,
                })],
            });
            Timeline.AttachPanes(Panes);
        }

        public MapViews Views { get; }

        public MapSplitViewModel Split { get; }

        public DatasetsViewModel Datasets { get; }

        public TimelinePanes Panes { get; }

        public TimelineViewModel Timeline { get; }

        public DatasetEntry Entry(string name) => Datasets.Entries.Single(e => e.Id.Value == $"{name}.h5");

        public TimelineLaneViewModel Lane(string name) => Lanes.Single(l => l.DatasetId == $"{name}.h5");

        public IReadOnlyList<TimelineLaneViewModel> Lanes => [.. Timeline.LaneGroups.SelectMany(g => g.Lanes)];

        /// <summary>Opens Map 2 and shows <c>first</c> only in Map 1 and <c>second</c> only in Map 2.</summary>
        public void SplitWithDifferentPanes()
        {
            Split.OpenSplit();
            Entry("first").SetVisibleIn(Map2, false);
            Entry("second").SetVisibleIn(Map1, false);
        }

        public void Dispose() => Panes.Dispose();
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

    private static int[] Chips(TimelineLaneViewModel lane) => [.. lane.Chips.Select(c => c.Number)];

    [AvaloniaFact]
    public void Differing_panes_show_the_union_with_chips_and_switching_dims_without_reflowing()
    {
        using var f = new Fixture();
        f.SplitWithDifferentPanes();
        using var host = ViewHost.Show(new TimelineView { DataContext = f.Timeline }, width: 900, height: 360);

        // The union: every lane, each with the chips of the panes that draw it.
        var order = f.Lanes.Select(l => l.Key).ToArray();
        Assert.Equal(["both.h5", "first.h5", "second.h5"], order);
        Assert.Equal([1, 2], Chips(f.Lane("both")));
        Assert.Equal([1], Chips(f.Lane("first")));
        Assert.Equal([2], Chips(f.Lane("second")));

        // In the view: one chip per drawing pane, named by its label.
        var chips = host.FindAll<Border>("Timeline.Lane.Chip");
        Assert.Equal(4, chips.Count);
        Assert.Equal(["Map 1", "Map 2", "Map 1", "Map 2"], chips.Select(AutomationProperties.GetName));
        Assert.Equal("Drawn in Map 2", ToolTip.GetTip(chips[^1]));

        // Map 2 (the new pane) is active: Map 1's own lane is dimmed in place.
        Assert.Equal(Map2, f.Split.ActivePane?.Id);
        Assert.Equal(new[] { 1.0, TimelineLaneViewModel.DimmedOpacity, 1.0 }, LaneOpacities(host));
        Assert.Contains("active", chips[^1].Classes);

        // Switching to Map 1 moves the dimming, not the lanes.
        var groups = f.Timeline.LaneGroups.ToArray();
        f.Split.Activate(Map1);
        host.Settle();

        Assert.Equal(order, f.Lanes.Select(l => l.Key));
        Assert.Equal(groups, f.Timeline.LaneGroups);
        Assert.Equal(new[] { 1.0, 1.0, TimelineLaneViewModel.DimmedOpacity }, LaneOpacities(host));
        Assert.Contains("active", host.FindAll<Border>("Timeline.Lane.Chip")[0].Classes);
    }

    /// <summary>The opacity of each lane row in the view, in order.</summary>
    private static double[] LaneOpacities(ViewHost host) =>
        [.. host.All<Grid>()
            .Where(g => g.DataContext is TimelineLaneViewModel && g.ColumnDefinitions.Count == 2 && g.GetVisualParent() is ContentPresenter)
            .Select(g => g.Opacity)];

    [AvaloniaFact]
    public void A_toggle_in_one_pane_updates_its_chip_and_the_dimming()
    {
        using var f = new Fixture();
        f.SplitWithDifferentPanes();

        f.Entry("both").SetVisibleIn(Map2, false);

        Assert.Equal([1], Chips(f.Lane("both")));
        Assert.True(f.Lane("both").IsDimmed);
        f.Entry("both").SetVisible(true);
        Assert.Equal([1, 2], Chips(f.Lane("both")));
        Assert.False(f.Lane("both").IsDimmed);
    }

    [AvaloniaFact]
    public void A_lane_hidden_everywhere_stays_dimmed_with_no_chips()
    {
        using var f = new Fixture();
        f.SplitWithDifferentPanes();

        f.Entry("first").SetVisible(false);

        Assert.Equal(3, f.Lanes.Count);
        Assert.Empty(f.Lane("first").Chips);
        Assert.True(f.Lane("first").IsDimmed);
    }

    [AvaloniaFact]
    public void One_pane_shows_no_chips_and_dims_a_hidden_lane()
    {
        using var f = new Fixture();

        f.Entry("first").SetVisible(false);

        Assert.All(f.Lanes, l => Assert.Empty(l.Chips));
        Assert.True(f.Lane("first").IsDimmed);
        Assert.False(f.Lane("both").IsDimmed);
    }

    [AvaloniaFact]
    public void Chips_follow_swapping_renaming_and_closing_the_split()
    {
        using var f = new Fixture();
        f.SplitWithDifferentPanes();

        f.Split.Swap();
        // Map 2 is now first in reading order, so it's pane 1.
        Assert.Equal([1], Chips(f.Lane("second")));
        Assert.Equal(Map2, f.Lane("second").Chips[0].Map);

        Assert.True(f.Split.SetName(Map2, "Low water"));
        Assert.Equal("Drawn in Low water", f.Lane("second").Chips[0].Tooltip);

        f.Split.CloseSplit();
        Assert.All(f.Lanes, l => Assert.Empty(l.Chips));
    }

    [AvaloniaFact]
    public void The_pane_source_answers_for_a_dataset_added_later_and_draws_an_unknown_one_everywhere()
    {
        using var f = new Fixture();
        f.SplitWithDifferentPanes();
        var raised = 0;
        f.Panes.Changed += () => raised++;
        var later = new DatasetEntry("/tmp/later.h5", "S-104");
        later.SetVisibleIn(Map1, false);

        f.Datasets.Entries.Add(later);

        Assert.True(raised > 0);
        Assert.False(f.Panes.Draws("later.h5", Map1));
        Assert.True(f.Panes.Draws("later.h5", Map2));
        Assert.True(f.Panes.Draws("unknown", Map1));
        later.IsHiddenByDisplay = true;
        Assert.False(f.Panes.Draws("later.h5", Map2));
    }
}
