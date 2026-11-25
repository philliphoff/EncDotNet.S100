using EncDotNet.S100.DataModel;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="MapViews"/> and <see cref="ActiveMapViewportNotifier"/> (split
/// map, #1730): the singletons that stand for "the map" follow the active map,
/// and the registry's viewport set covers every map.
/// </summary>
public sealed class MapViewsTests
{
    private static MapViewportSnapshot Viewport(double south, double west) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = south + 1,
        MaxLongitude = west + 1,
        MercatorResolution = 5,
    };

    private sealed class Fixture
    {
        public MapCapabilityAccessor<IMapViewportController> Viewport { get; } = new();
        public MapCapabilityAccessor<IMapCoordinateConverter> Converter { get; } = new();
        public MapCapabilityAccessor<IImageRenderer> Renderer { get; } = new();
        public DemandPlannerSet Planners { get; } = new();
        public MapViews Views { get; }

        public Fixture() => Views = new MapViews(Viewport, Converter, Renderer, Planners);

        public void AssertBoundTo(FakeMapView? view)
        {
            Assert.Same(view?.Host, Viewport.Current);
            Assert.Same(view?.Host, Converter.Current);
            Assert.Same(view?.Host, Renderer.Current);
        }
    }

    [Fact]
    public void The_first_map_added_becomes_active_and_binds_the_accessors()
    {
        var f = new Fixture();
        var first = new FakeMapView("map-1");
        var activeChanges = 0;
        f.Views.ActiveChanged += (_, _) => activeChanges++;

        f.Views.Add(first);
        f.Views.Add(new FakeMapView("map-2"));

        Assert.Same(first, f.Views.Active);
        Assert.Equal(1, activeChanges);
        f.AssertBoundTo(first);
        Assert.Equal(2, f.Views.All.Count);
    }

    [Fact]
    public void Activating_a_map_rebinds_the_accessors_before_ActiveChanged()
    {
        var f = new Fixture();
        var first = new FakeMapView("map-1");
        var second = new FakeMapView("map-2");
        f.Views.Add(first);
        f.Views.Add(second);
        IImageRenderer? rendererSeen = null;
        f.Views.ActiveChanged += (_, _) => rendererSeen = f.Renderer.Current;

        f.Views.Activate(second);

        Assert.Same(second, f.Views.Active);
        f.AssertBoundTo(second);
        Assert.Same(second.Host, rendererSeen);

        f.Views.Activate(first);
        f.AssertBoundTo(first);
    }

    [Fact]
    public void Removing_the_active_map_moves_active_and_removing_the_last_clears_the_accessors()
    {
        var f = new Fixture();
        var first = new FakeMapView("map-1");
        var second = new FakeMapView("map-2");
        f.Views.Add(first);
        f.Views.Add(second);
        f.Views.Activate(second);

        Assert.True(f.Views.Remove(second));
        Assert.Same(first, f.Views.Active);
        f.AssertBoundTo(first);

        Assert.True(f.Views.Remove(first));
        Assert.Null(f.Views.Active);
        f.AssertBoundTo(null);
        Assert.False(f.Views.Remove(first));
    }

    [Fact]
    public void Each_maps_planner_joins_the_shared_set_while_registered()
    {
        var f = new Fixture();
        var first = new FakeMapView("map-1");
        var second = new FakeMapView("map-2");

        f.Views.Add(first);
        f.Views.Add(second);
        Assert.Equal([first.Planner, second.Planner], f.Planners.Planners);

        f.Views.Remove(first);
        Assert.Equal([second.Planner], f.Planners.Planners);
    }

    [Fact]
    public void A_duplicate_id_or_an_unregistered_map_is_refused()
    {
        var f = new Fixture();
        f.Views.Add(new FakeMapView("map-1"));

        Assert.Throws<InvalidOperationException>(() => f.Views.Add(new FakeMapView("map-1")));
        Assert.Throws<ArgumentException>(() => f.Views.Activate(new FakeMapView("map-9")));
    }

    [Fact]
    public void The_viewport_set_holds_every_map_and_follows_an_inactive_map_moving()
    {
        var f = new Fixture();
        var first = new FakeMapView("map-1");
        var second = new FakeMapView("map-2");
        first.Notifier.Publish(Viewport(40, -75));
        f.Views.Add(first);
        f.Views.Add(second);
        IReadOnlyList<MapViewViewport>? raised = null;
        f.Views.ViewportsChanged += (_, v) => raised = v;

        // The second map isn't active, and it still counts.
        second.Notifier.Publish(Viewport(50, -5));

        Assert.NotNull(raised);
        Assert.Equal(
            [new MapViewViewport(first.Id, Viewport(40, -75)), new MapViewViewport(second.Id, Viewport(50, -5))],
            raised);
        Assert.Equal(raised, f.Views.Current);

        // A removed map drops out and stops raising.
        f.Views.Remove(second);
        Assert.Equal([new MapViewViewport(first.Id, Viewport(40, -75))], raised);
        raised = null;
        second.Notifier.Publish(Viewport(10, 10));
        Assert.Null(raised);
    }

    [Fact]
    public void The_active_notifier_follows_the_active_map()
    {
        var f = new Fixture();
        var first = new FakeMapView("map-1");
        var second = new FakeMapView("map-2");
        first.Notifier.Publish(Viewport(40, -75));
        second.Notifier.Publish(Viewport(50, -5));
        using var notifier = new ActiveMapViewportNotifier(f.Views);
        var raised = new List<MapViewportSnapshot>();
        notifier.ViewportChanged += (_, s) => raised.Add(s);
        Assert.Null(notifier.Current);

        // Adding the first map makes it active: the switch reads as a move.
        f.Views.Add(first);
        f.Views.Add(second);
        Assert.Equal(Viewport(40, -75), notifier.Current);
        Assert.Equal([Viewport(40, -75)], raised);

        // The inactive map moving isn't the active map's viewport.
        second.Notifier.Publish(Viewport(51, -5));
        Assert.Single(raised);

        f.Views.Activate(second);
        Assert.Equal(Viewport(51, -5), notifier.Current);
        Assert.Equal(Viewport(51, -5), raised[^1]);

        // Now the first map's moves are ignored and the second's come through.
        raised.Clear();
        first.Notifier.Publish(Viewport(41, -75));
        second.Notifier.Publish(Viewport(52, -5));
        Assert.Equal([Viewport(52, -5)], raised);

        f.Views.Remove(second);
        f.Views.Remove(first);
        Assert.Null(notifier.Current);
    }

    internal sealed class FakeMapView(string id) : IMapViewCapabilities
    {
        public MapViewId Id { get; } = new(id);

        public FakeHost Host { get; } = new();

        public FakeNotifier Notifier { get; } = new();

        public IMapViewportController ViewportController => Host;

        public IMapCoordinateConverter CoordinateConverter => Host;

        public IImageRenderer ImageRenderer => Host;

        public IMapViewportNotifier Viewport => Notifier;

        public DemandPlanner Planner { get; } = new();
    }

    internal sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }

        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(MapViewportSnapshot snapshot)
        {
            Current = snapshot;
            ViewportChanged?.Invoke(this, snapshot);
        }
    }

    internal sealed class FakeHost : IMapViewportController, IMapCoordinateConverter, IImageRenderer
    {
        public void ZoomToExtent(MRect extent) { }

        public void SetViewportToExtent(MRect mercatorExtent) { }

        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }

        public void SetRotation(double degrees) { }

        public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300) { }

        public GeoPosition? TryGetViewportCenterWgs84() => null;

        public (double Width, double Height)? TryGetViewportSizePx() => null;

        public GeoPosition? TryScreenToWgs84(double xPx, double yPx) => null;

        public GeoPosition? TryImagePixelToWgs84(double xPx, double yPx, int imageWidthPx, int imageHeightPx) => null;

        public Task<byte[]?> RenderToPngAsync(
            int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
    }
}
