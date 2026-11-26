using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="MapViewportLink"/> (split map, #1734; design note D13, D14;
/// architecture §5 question 1): linked maps share the centre and keep the
/// ratio of their scales from the moment of linking, whichever map moves;
/// unlinking moves nothing; and a follower's planner sees the driver's motion
/// and destinations, mapped into it.
/// </summary>
public sealed class MapViewportLinkTests
{
    // An overview of the Sound and a detail at Shilshole, 1:16 apart.
    private const double OverviewResolution = 40;
    private const double DetailResolution = 2.5;

    private sealed class Fixture
    {
        public Fixture()
        {
            Views = new MapViews(
                new MapCapabilityAccessor<IMapViewportController>(),
                new MapCapabilityAccessor<IMapCoordinateConverter>(),
                new MapCapabilityAccessor<IImageRenderer>(),
                new DemandPlannerSet());
            Link = new MapViewportLink(Views);
        }

        public MapViews Views { get; }

        public MapViewportLink Link { get; }

        public FakeLinkableMap Add(string id, double? x = null, double? y = null, double? resolution = null)
        {
            var map = new FakeLinkableMap(id);
            if (x is { } cx && y is { } cy && resolution is { } r)
                map.Place(cx, cy, r);
            Views.Add(map);
            return map;
        }
    }

    /// <summary>
    /// A map whose navigator is a field: it reports every view it shows to
    /// its planner, as <c>MapsuiMapNavigator</c> does, and can hold a
    /// smallest resolution, as a zoom limit does.
    /// </summary>
    private sealed class FakeLinkableMap(string id) : IMapViewCapabilities, ILinkableMapView
    {
        private readonly MapViewsTests.FakeMapView _inner = new(id);

        public MapViewId Id => _inner.Id;

        public IMapViewportController ViewportController => _inner.ViewportController;

        public IMapCoordinateConverter CoordinateConverter => _inner.CoordinateConverter;

        public IImageRenderer ImageRenderer => _inner.ImageRenderer;

        public IMapViewportNotifier Viewport => _inner.Viewport;

        public DemandPlanner Planner { get; } = new();

        public IMapDatasetSession DatasetSession => _inner.DatasetSession;

        public ViewState? CurrentView { get; private set; }

        public double MinimumResolution { get; set; }

        public Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

        public event EventHandler? ViewChanged;

        public void ShowView(double centerX, double centerY, double resolution) =>
            Place(centerX, centerY, resolution);

        /// <summary>The user (or a fling, or an animation) moves this map.</summary>
        public void Place(double centerX, double centerY, double resolution)
        {
            CurrentView = new ViewState(centerX, centerY, Math.Max(resolution, MinimumResolution), 0, 600, 400);
            Planner.Observe(CurrentView.Value, Clock());
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Lay(double width, double height, double centerX, double centerY, double resolution)
        {
            CurrentView = new ViewState(centerX, centerY, resolution, 0, width, height);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AssertAt(double centerX, double centerY, double resolution)
        {
            var view = Assert.NotNull(CurrentView);
            Assert.Equal(centerX, view.CenterX, 6);
            Assert.Equal(centerY, view.CenterY, 6);
            Assert.Equal(resolution, view.Resolution, 9);
        }
    }

    [Fact]
    public void The_link_starts_off_and_moves_nothing()
    {
        var f = new Fixture();
        var overview = f.Add("map-1", -13_620_000, 6_040_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);

        overview.Place(-13_600_000, 6_000_000, OverviewResolution * 2);

        Assert.False(f.Link.IsLinked);
        detail.AssertAt(-13_617_000, 6_050_000, DetailResolution);
    }

    [Fact]
    public void Linking_centres_the_others_on_the_active_map_and_keeps_every_scale()
    {
        // D14: "Centre and scale" would jump one pane to the other's scale.
        var f = new Fixture();
        var overview = f.Add("map-1", -13_620_000, 6_040_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(detail);

        f.Link.SetLinked(true);

        Assert.True(f.Link.IsLinked);
        detail.AssertAt(-13_617_000, 6_050_000, DetailResolution);
        overview.AssertAt(-13_617_000, 6_050_000, OverviewResolution);
        Assert.Equal(1, f.Link.RatioOf(detail.Id));
        Assert.Equal(OverviewResolution / DetailResolution, f.Link.RatioOf(overview.Id));
    }

    [Fact]
    public void Panning_and_zooming_one_linked_map_moves_the_others_at_their_ratio()
    {
        var f = new Fixture();
        var overview = f.Add("map-1", -13_620_000, 6_040_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(detail);
        f.Link.SetLinked(true);

        // Pan the detail: the overview pans to the same centre.
        detail.Place(-13_616_000, 6_051_000, DetailResolution);
        overview.AssertAt(-13_616_000, 6_051_000, OverviewResolution);

        // Zoom the detail in twice over: the overview zooms by the same factor.
        detail.Place(-13_616_000, 6_051_000, DetailResolution / 2);
        overview.AssertAt(-13_616_000, 6_051_000, OverviewResolution / 2);

        // Either map drives: zoom the overview out four times over.
        overview.Place(-13_630_000, 6_030_000, OverviewResolution * 2);
        detail.AssertAt(-13_630_000, 6_030_000, DetailResolution * 2);
    }

    [Fact]
    public void Two_maps_linked_at_the_same_scale_stay_identical()
    {
        // D14: the S-57/S-101 and low/high-water comparisons.
        var f = new Fixture();
        var left = f.Add("map-1", -13_617_000, 6_050_000, DetailResolution);
        var right = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Link.SetLinked(true);

        for (var i = 1; i <= 10; i++)
            left.Place(-13_617_000 + (i * 37.5), 6_050_000 - (i * 12.25), DetailResolution * (1 + (i * 0.1)));

        Assert.Equal(left.CurrentView!.Value.CenterX, right.CurrentView!.Value.CenterX, 6);
        Assert.Equal(left.CurrentView!.Value.CenterY, right.CurrentView!.Value.CenterY, 6);
        Assert.Equal(left.CurrentView!.Value.Resolution, right.CurrentView!.Value.Resolution, 9);
    }

    [Fact]
    public void A_follower_held_by_its_zoom_limit_rejoins_its_ratio()
    {
        // The link maps the driver's view through the ratios; it doesn't add up steps.
        var f = new Fixture();
        var overview = f.Add("map-1", -13_617_000, 6_050_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(overview);
        f.Link.SetLinked(true);
        detail.MinimumResolution = 1;

        overview.Place(-13_617_000, 6_050_000, OverviewResolution / 8);
        detail.AssertAt(-13_617_000, 6_050_000, 1);

        overview.Place(-13_617_000, 6_050_000, OverviewResolution);
        detail.AssertAt(-13_617_000, 6_050_000, DetailResolution);
        Assert.Equal(OverviewResolution / DetailResolution, f.Link.RatioOf(overview.Id)!.Value / f.Link.RatioOf(detail.Id)!.Value, 9);
    }

    [Fact]
    public void Unlinking_leaves_every_map_where_it_is()
    {
        var f = new Fixture();
        var overview = f.Add("map-1", -13_620_000, 6_040_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(detail);
        f.Link.SetLinked(true);
        detail.Place(-13_616_000, 6_051_000, DetailResolution / 2);
        var changes = 0;
        f.Link.IsLinkedChanged += (_, _) => changes++;

        f.Link.SetLinked(false);

        Assert.False(f.Link.IsLinked);
        Assert.Equal(1, changes);
        detail.AssertAt(-13_616_000, 6_051_000, DetailResolution / 2);
        overview.AssertAt(-13_616_000, 6_051_000, OverviewResolution / 2);
        Assert.Null(f.Link.RatioOf(overview.Id));

        // And the maps are independent again.
        detail.Place(-13_600_000, 6_060_000, DetailResolution);
        overview.AssertAt(-13_616_000, 6_051_000, OverviewResolution / 2);
    }

    [Fact]
    public void Relinking_takes_the_ratio_at_that_moment()
    {
        var f = new Fixture();
        var left = f.Add("map-1", -13_617_000, 6_050_000, DetailResolution);
        var right = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(left);
        f.Link.SetLinked(true);
        f.Link.SetLinked(false);
        right.Place(-13_617_000, 6_050_000, DetailResolution * 4);

        f.Link.SetLinked(true);
        left.Place(-13_617_000, 6_050_000, DetailResolution * 2);

        right.AssertAt(-13_617_000, 6_050_000, DetailResolution * 8);
    }

    [Fact]
    public void A_map_that_opens_while_linked_joins_at_the_active_ratio_once_laid_out()
    {
        // D2: a new pane opens at the active pane's view, then it's laid out.
        var f = new Fixture();
        var overview = f.Add("map-1", -13_620_000, 6_040_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(overview);
        f.Link.SetLinked(true);

        var third = f.Add("map-3");
        // Mapsui's first sizing frames the map's home: the link moves it instead.
        third.Lay(600, 400, 0, 0, 78_000);

        third.AssertAt(-13_620_000, 6_040_000, OverviewResolution);
        overview.AssertAt(-13_620_000, 6_040_000, OverviewResolution);
        detail.AssertAt(-13_620_000, 6_040_000, DetailResolution);

        // And it drives like any other.
        third.Place(-13_610_000, 6_045_000, OverviewResolution * 2);
        detail.AssertAt(-13_610_000, 6_045_000, DetailResolution * 2);
    }

    [Fact]
    public void A_closed_map_leaves_the_link()
    {
        var f = new Fixture();
        var left = f.Add("map-1", -13_617_000, 6_050_000, DetailResolution);
        var right = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Link.SetLinked(true);

        f.Views.Remove(right);
        left.Place(-13_600_000, 6_060_000, DetailResolution);

        right.AssertAt(-13_617_000, 6_050_000, DetailResolution);
        Assert.Null(f.Link.RatioOf(right.Id));
    }

    [Fact]
    public void A_followers_planner_sees_copied_motion_not_a_jump()
    {
        // §5 Q1: the follower is set every frame the driver moves, so its own
        // history has a velocity, at its own scale, and it predicts ahead.
        var f = new Fixture();
        var overview = f.Add("map-1", -13_617_000, 6_050_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(detail);
        f.Link.SetLinked(true);
        var clock = Stopwatch.GetTimestamp();
        var frame = Stopwatch.Frequency / 60;
        overview.Clock = detail.Clock = () => clock;

        // Drag the detail east at 6 km/s (40 px a frame) for half a second.
        for (var i = 1; i <= 30; i++)
        {
            clock += frame;
            detail.Place(-13_617_000 + (i * 100.0), 6_050_000, DetailResolution);
        }

        var plan = overview.Planner.Plan(clock);
        var ahead = plan.Views.Where(v => v.Source == PredictionSource.Motion).ToList();
        Assert.NotEmpty(ahead);
        Assert.All(ahead, v =>
        {
            Assert.True(v.View.CenterX > -13_617_000 + 3_000, "The follower predicts the drag going on east.");
            Assert.Equal(OverviewResolution, v.View.Resolution, 9);
        });
        Assert.Equal(ViewMotionKind.Idle, plan.Motion.Kind);
    }

    [Fact]
    public void A_followers_planner_gets_the_drivers_fling_destination_at_its_ratio()
    {
        // §5 Q1: the driver's predicted resting view, mapped into the follower,
        // ending when the driver's fling does: the follower pre-fetches there.
        var f = new Fixture();
        var overview = f.Add("map-1", -13_617_000, 6_050_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);
        f.Views.Activate(detail);
        f.Link.SetLinked(true);
        var replans = 0;
        overview.Planner.DestinationChanged += (_, _) => replans++;

        var start = Stopwatch.GetTimestamp();
        var target = detail.Planner.BeginFling(500, -250, TimeSpan.FromSeconds(1), start);

        var driverTarget = Assert.NotNull(target);
        var motion = overview.Planner.Motion;
        Assert.Equal(ViewMotionKind.Animated, motion.Kind);
        var followerTarget = Assert.NotNull(motion.Target);
        Assert.Equal(driverTarget.CenterX, followerTarget.CenterX, 6);
        Assert.Equal(driverTarget.CenterY, followerTarget.CenterY, 6);
        Assert.Equal(OverviewResolution, followerTarget.Resolution, 9);
        Assert.Equal(overview.CurrentView!.Value.Width, followerTarget.Width);
        var arrival = start + Stopwatch.Frequency;
        Assert.InRange(motion.ArrivalTimestamp, arrival - (Stopwatch.Frequency / 10), arrival + (Stopwatch.Frequency / 10));
        Assert.Equal(1, replans);
        Assert.Contains(
            overview.Planner.Plan().Views,
            v => v.Source == PredictionSource.Destination && v.View == followerTarget);

        // The follower's own report doesn't echo back to the driver.
        Assert.Equal(ViewMotionKind.Fling, detail.Planner.Motion.Kind);
    }

    [Fact]
    public void An_unlinked_map_gets_no_destination()
    {
        var f = new Fixture();
        var overview = f.Add("map-1", -13_617_000, 6_050_000, OverviewResolution);
        var detail = f.Add("map-2", -13_617_000, 6_050_000, DetailResolution);

        detail.Planner.BeginFling(500, -250, TimeSpan.FromSeconds(1), Stopwatch.GetTimestamp());

        Assert.Equal(ViewMotionKind.Idle, overview.Planner.Motion.Kind);
    }
}
