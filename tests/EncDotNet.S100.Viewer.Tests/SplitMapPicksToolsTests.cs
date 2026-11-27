using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Picks, tools and the cursor echo across the panes of a split map
/// (#1733; split map design note D10–D12, D15, D16).
/// </summary>
public sealed class SplitMapPicksToolsTests
{
    private const string DatasetId = "US5WA10M.000";

    /// <summary>A map's layers, as the overlays add them.</summary>
    private sealed class FakeLayers : IMapLayerCollection
    {
        public List<ILayer> Overlays { get; } = [];

        public List<ILayer> Tools { get; } = [];

        public void SetBasemapLayer(ILayer? layer) { }

        public void AddOverlayLayer(ILayer layer) => Overlays.Add(layer);

        public void RemoveOverlayLayer(ILayer layer) => Overlays.Remove(layer);

        public void AddToolLayer(ILayer layer) => Tools.Add(layer);

        public void RemoveToolLayer(ILayer layer) => Tools.Remove(layer);

        public MemoryLayer Overlay(string name) => (MemoryLayer)Overlays.Single(l => l.Name == name);
    }

    private sealed class NoDatasets : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => [];

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

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

    private sealed record Split(
        MapViews Views,
        MapSplitViewModel Panes,
        MapViewsTests.FakeMapView One,
        MapViewsTests.FakeMapView Two)
    {
        public MapSplitServices Services { get; } = new(
            Views, Panes, new MapStatusFocus(Views), new MapCursorEcho(), new PickPresence(Views));
    }

    /// <summary>Two panes over the same dataset, as step 1 has them.</summary>
    private static Split TwoPanes()
    {
        var views = new MapViews(
            new MapCapabilityAccessor<IMapViewportController>(),
            new MapCapabilityAccessor<IMapCoordinateConverter>(),
            new MapCapabilityAccessor<IImageRenderer>(),
            new DemandPlannerSet());
        var split = new MapSplitViewModel(views, new Host(views), () => { });
        split.Restore(saved: null, restoreViews: true);
        split.OpenSplit();
        var one = (MapViewsTests.FakeMapView)split.Panes[0].View;
        var two = (MapViewsTests.FakeMapView)split.Panes[1].View;
        foreach (var view in new[] { one, two })
            view.Session.SetDataset(Dataset(), minimumDisplayScale: null, maximumDisplayScale: null);
        return new Split(views, split, one, two);
    }

    private static MapDataset Dataset() => new(
        new MapDatasetId(DatasetId),
        DatasetId,
        new DatasetMetadata
        {
            Spec = new SpecRef("S-57", new SpecVersion(3, 1, 0)),
            Extent = new BoundingBox(47.6, -122.5, 47.8, -122.3),
        });

    private static PickHit Light() => new()
    {
        FeatureType = "LIGHTS",
        FeatureTypeName = "Light",
        FeatureRef = "LIGHTS-1",
        DatasetFileName = DatasetId,
        DatasetId = DatasetId,
        ProductSpec = "S-57",
    };

    private static void PickLight(PickReportViewModel report) =>
        report.SetPicks([Light()], location: new PickLocation(47.68, -122.41));

    private static PickHighlightController Highlight(
        FakeLayers layers, PickReportViewModel report, Split split, IMapViewCapabilities view) =>
        new(
            layers,
            report,
            new NoDatasets(),
            new StubMeasureOverlayAppearanceProvider(),
            new ViewerSettingsService(new ViewerSettings()),
            a => a(),
            warningDrawing: null,
            ghostReason: hit => split.Services.Picks.GhostReason(view, hit));

    private static IEnumerable<Pen> Pens(MemoryLayer layer) =>
        layer.Features.SelectMany(f => f.Styles)
            .SelectMany(s => s switch
            {
                SymbolStyle symbol when symbol.Outline is { } outline => [outline],
                VectorStyle vector when vector.Line is { } line => [line],
                _ => Array.Empty<Pen>(),
            });

    private static string[] Labels(MemoryLayer layer) =>
        layer.Features.SelectMany(f => f.Styles.OfType<LabelStyle>().Select(l => l.GetLabelText(f) ?? string.Empty)).ToArray();

    private static MapViewportSnapshot View(double south, double west, double north, double east) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = north,
        MaxLongitude = east,
        MercatorResolution = 10,
    };

    [AvaloniaFact]
    public void A_pick_is_outlined_in_both_panes()
    {
        // D10: a pick is about a feature, so every pane that draws it shows it.
        var split = TwoPanes();
        var report = TestPickReport.Create();
        var oneLayers = new FakeLayers();
        var twoLayers = new FakeLayers();
        using var one = Highlight(oneLayers, report, split, split.One);
        using var two = Highlight(twoLayers, report, split, split.Two);

        PickLight(report);

        foreach (var layers in new[] { oneLayers, twoLayers })
        {
            var layer = layers.Overlay(PickHighlightOverlayLayer.LayerName);
            Assert.NotEmpty(layer.Features);
            Assert.All(Pens(layer), pen => Assert.Equal(PenStyle.Solid, pen.PenStyle));
            Assert.Empty(Labels(layer));
        }
    }

    [AvaloniaFact]
    public void A_pane_that_doesnt_draw_the_picked_feature_shows_a_ghost_with_the_reason()
    {
        // D10, steps 2 and 3: the location is in view but the feature isn't
        // drawn there. Step 1 never gets here; this drives the path by hand.
        var split = TwoPanes();
        split.Two.Session.RemoveDatasets([new MapDatasetId(DatasetId)], preserveState: false, removeProcessor: false);
        var report = TestPickReport.Create();
        var oneLayers = new FakeLayers();
        var twoLayers = new FakeLayers();
        using var one = Highlight(oneLayers, report, split, split.One);
        using var two = Highlight(twoLayers, report, split, split.Two);

        PickLight(report);

        var ghost = twoLayers.Overlay(PickHighlightOverlayLayer.LayerName);
        Assert.Contains(Pens(ghost), pen => pen.PenStyle == PenStyle.Dash);
        Assert.Equal(["S-57 hidden in this map"], Labels(ghost));
        var drawn = oneLayers.Overlay(PickHighlightOverlayLayer.LayerName);
        Assert.All(Pens(drawn), pen => Assert.Equal(PenStyle.Solid, pen.PenStyle));
        Assert.Empty(Labels(drawn));
    }

    [AvaloniaFact]
    public void No_pane_shows_a_ghost_when_no_map_draws_the_feature()
    {
        var split = TwoPanes();
        foreach (var view in new[] { split.One, split.Two })
            view.Session.RemoveDatasets([new MapDatasetId(DatasetId)], preserveState: false, removeProcessor: false);

        Assert.Null(split.Services.Picks.GhostReason(split.Two, Light()));
    }

    [AvaloniaFact]
    public void Object_information_says_which_maps_show_the_pick()
    {
        // D11: chips under the title while split; a hidden chip selects the dataset.
        var split = TwoPanes();
        var report = TestPickReport.Create();
        var selected = new List<string>();
        using var presenter = new PickMapPresencePresenter(
            report, split.Services, new TestStationCharts.FixedTimeFormat(TimeFormat.Utc), selected.Add);

        PickLight(report);

        Assert.Equal(["Shown in Map 1", "Shown in Map 2"], report.ShownIn.Select(c => c.Text));
        Assert.False(report.HasOtherMaps);

        split.Two.Session.RemoveDatasets([new MapDatasetId(DatasetId)], preserveState: false, removeProcessor: false);
        PickLight(report);

        var hidden = report.ShownIn[1];
        Assert.Equal("Hidden in Map 2", hidden.Text);
        Assert.Equal("PickReport.ShownIn.map-2", hidden.AutomationId);
        hidden.Command!.Execute(null);
        Assert.Equal([DatasetId], selected);

        split.Panes.CloseSplit();
        Assert.False(report.HasShownIn);
    }

    [AvaloniaFact]
    public void Object_information_lists_the_other_maps_at_another_time()
    {
        // D11, step 3: a map at another time gets an Other maps row.
        var split = TwoPanes();
        split.Views.Activate(split.One);
        split.Two.Session.TimeSnapshot = new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Current = new DateTime(2026, 11, 26, 14, 18, 0, DateTimeKind.Utc),
        };
        var report = TestPickReport.Create();
        using var presenter = new PickMapPresencePresenter(
            report, split.Services, new TestStationCharts.FixedTimeFormat(TimeFormat.Utc), _ => { });

        PickLight(report);

        var row = Assert.Single(report.OtherMaps);
        Assert.Equal("Map 2 · 14:18 UTC", row.Text);
        Assert.Equal("PickReport.OtherMaps.map-2", row.AutomationId);
    }

    [AvaloniaFact]
    public void A_pick_in_a_map_that_isnt_active_is_reported_at_that_map()
    {
        // #1763, D11: a pick selected in a map without activating it (MCP
        // pick_features with select and map) is reported at that map, not at
        // the active one. The panes differ: the active map hides the dataset
        // and is at another time.
        var split = TwoPanes();
        split.Views.Activate(split.One);
        split.One.Session.RemoveDatasets([new MapDatasetId(DatasetId)], preserveState: false, removeProcessor: false);
        split.One.Session.TimeSnapshot = new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Current = new DateTime(2026, 11, 26, 14, 18, 0, DateTimeKind.Utc),
        };
        split.Two.Session.TimeSnapshot = new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Current = new DateTime(2026, 11, 26, 9, 30, 0, DateTimeKind.Utc),
        };
        var report = TestPickReport.Create();
        var oneLayers = new FakeLayers();
        var twoLayers = new FakeLayers();
        using var one = Highlight(oneLayers, report, split, split.One);
        using var two = Highlight(twoLayers, report, split, split.Two);
        using var presenter = new PickMapPresencePresenter(
            report, split.Services, new TestStationCharts.FixedTimeFormat(TimeFormat.Utc), _ => { });

        report.SetPicks(new PickResult([Light()], new PickLocation(47.68, -122.41), Map: split.Two.Id));

        Assert.Same(split.One, split.Views.Active);
        Assert.Equal(split.Two.Id, report.PickedMap);
        Assert.Equal(["Hidden in Map 1", "Shown in Map 2"], report.ShownIn.Select(c => c.Text));
        // Reported at Map 2's time, so the active Map 1 is the other map.
        var row = Assert.Single(report.OtherMaps);
        Assert.Equal("Map 1 · 14:18 UTC", row.Text);
        // The active map, which hides the dataset, shows the ghost; the
        // picked map outlines the feature.
        var ghost = oneLayers.Overlay(PickHighlightOverlayLayer.LayerName);
        Assert.Contains(Pens(ghost), pen => pen.PenStyle == PenStyle.Dash);
        Assert.Equal(["S-57 hidden in this map"], Labels(ghost));
        var drawn = twoLayers.Overlay(PickHighlightOverlayLayer.LayerName);
        Assert.NotEmpty(drawn.Features);
        Assert.All(Pens(drawn), pen => Assert.Equal(PenStyle.Solid, pen.PenStyle));
        Assert.Empty(Labels(drawn));

        // A later pick that names no map (feature search) is reported at the
        // active map.
        report.SetPicks(new PickResult([Light()], new PickLocation(47.69, -122.42)));

        Assert.Null(report.PickedMap);
        Assert.Equal("Map 2 · 09:30 UTC", Assert.Single(report.OtherMaps).Text);
    }

    [AvaloniaFact]
    public void A_measurement_started_in_one_pane_is_finished_in_the_other_and_drawn_in_both()
    {
        // D12, O2: the tools take clicks from any pane; the path is geographic.
        var left = new MapControl { Width = 300, Height = 300 };
        var right = new MapControl { Width = 300, Height = 300 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,300") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        using var host = ViewHost.Show(grid, width: 600, height: 300);

        // Each pane shows its own place: the left one a wide view, the right
        // one 1° further east.
        static MapToolContext Context(MapControl control, double west) => new(
            control,
            _ => { },
            p => new GeoPosition(48 - (p.Y / 100), west + (p.X / 100)),
            g => new Point((g.Longitude - west) * 100, (48 - g.Latitude) * 100));
        var leftContext = Context(left, -123);
        var rightContext = Context(right, -122);

        var measure = new MeasureSession(settings: null);
        var rangeBearing = new RangeBearingState(settings: null);
        var tools = new MapToolController();
        tools.Register(new MeasureTool(measure));
        tools.Initialize(leftContext);
        tools.Activate(MeasureTool.ToolId);

        // What MapInteractionController does for each map: forward the
        // pointer with that map's context.
        foreach (var (control, context) in new[] { (left, leftContext), (right, rightContext) })
        {
            control.AddHandler(InputElement.PointerPressedEvent, (_, e) => tools.OnPointerPressed(context, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, (_, e) => tools.OnPointerMoved(context, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, (_, e) => tools.OnPointerReleased(context, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        var leftLayers = new FakeLayers();
        var rightLayers = new FakeLayers();
        var settings = new ViewerSettingsService(new ViewerSettings());
        using var leftOverlay = new ToolOverlayController(
            leftLayers, measure, rangeBearing, new StubMeasureOverlayAppearanceProvider(), settings, a => a(), () => { });
        using var rightOverlay = new ToolOverlayController(
            rightLayers, measure, rangeBearing, new StubMeasureOverlayAppearanceProvider(), settings, a => a(), () => { });

        void Click(double x, double y)
        {
            host.Window.MouseMove(new Point(x, y));
            host.Window.MouseDown(new Point(x, y), MouseButton.Left);
            host.Window.MouseUp(new Point(x, y), MouseButton.Left);
            host.Settle();
        }

        Click(50, 50);   // left pane: 47.5 N, 122.5 W
        Click(450, 100); // right pane: 47 N, 120.5 W

        Assert.Equal(2, measure.State.Waypoints.Count);
        Assert.Equal(-122.5, measure.State.Waypoints[0].Longitude, 6);
        Assert.Equal(47.5, measure.State.Waypoints[0].Latitude, 6);
        Assert.Equal(-120.5, measure.State.Waypoints[1].Longitude, 6);
        Assert.Equal(47.0, measure.State.Waypoints[1].Latitude, 6);
        Assert.Same(rightContext, tools.Context);
        // The one measurement is drawn in both panes.
        Assert.NotEmpty(((MemoryLayer)leftLayers.Tools.Single(l => l.Name == MeasureOverlayLayer.LayerName)).Features);
        Assert.NotEmpty(((MemoryLayer)rightLayers.Tools.Single(l => l.Name == MeasureOverlayLayer.LayerName)).Features);
    }

    [AvaloniaFact]
    public void A_pane_outlines_a_much_smaller_pane_inside_it()
    {
        // D15: map 2 (a detail) lies in map 1 (an overview) and is narrower
        // than a quarter of it, so map 1 draws its box with its number.
        var split = TwoPanes();
        var oneLayers = new FakeLayers();
        var twoLayers = new FakeLayers();
        using var one = Overlays(oneLayers, split, split.One);
        using var two = Overlays(twoLayers, split, split.Two);

        split.One.Notifier.Publish(View(47.0, -123.0, 48.0, -122.0));
        split.Two.Notifier.Publish(View(47.6, -122.5, 47.7, -122.4));

        var boxes = oneLayers.Overlay(SplitMapOverlayLayer.ExtentLayerName);
        Assert.Equal(["2"], Labels(boxes));
        Assert.Contains(boxes.Features, f => f is GeometryFeature { Geometry: NetTopologySuite.Geometries.LineString });
        Assert.Empty(twoLayers.Overlay(SplitMapOverlayLayer.ExtentLayerName).Features);

        // Wider than a quarter of map 1: no box.
        split.Two.Notifier.Publish(View(47.2, -122.8, 47.8, -122.4));
        Assert.Empty(boxes.Features);
    }

    [Fact]
    public void Extent_boxes_need_the_other_pane_inside_and_narrower_than_a_quarter()
    {
        var own = View(47.0, -123.0, 48.0, -122.0);

        Assert.Single(SplitMapOverlayLayer.ExtentBoxes(own, [new PaneExtentBox(2, View(47.5, -122.6, 47.6, -122.5))]));
        Assert.Empty(SplitMapOverlayLayer.ExtentBoxes(own, [new PaneExtentBox(2, View(47.5, -122.1, 47.6, -121.9))]));
        Assert.Empty(SplitMapOverlayLayer.ExtentBoxes(own, [new PaneExtentBox(2, View(47.2, -122.9, 47.9, -122.1))]));
    }

    [AvaloniaFact]
    public void The_cursor_is_echoed_in_the_other_pane_where_it_is_in_view()
    {
        // D16: a quiet crosshair at the pointer's position in every other pane.
        var split = TwoPanes();
        var oneLayers = new FakeLayers();
        var twoLayers = new FakeLayers();
        using var one = Overlays(oneLayers, split, split.One);
        using var two = Overlays(twoLayers, split, split.Two);
        split.One.Notifier.Publish(View(47.0, -123.0, 48.0, -122.0));
        split.Two.Notifier.Publish(View(47.6, -122.5, 47.7, -122.4));

        split.Services.CursorEcho.Report(split.One.Id, new GeoPosition(47.65, -122.45));

        Assert.Equal(4, twoLayers.Overlay(SplitMapOverlayLayer.EchoLayerName).Features.Count());
        Assert.Empty(oneLayers.Overlay(SplitMapOverlayLayer.EchoLayerName).Features);

        // Out of map 2's view: no echo there.
        split.Services.CursorEcho.Report(split.One.Id, new GeoPosition(47.2, -122.9));
        Assert.Empty(twoLayers.Overlay(SplitMapOverlayLayer.EchoLayerName).Features);

        // Over map 2: echoed in map 1.
        split.Services.CursorEcho.Report(split.Two.Id, new GeoPosition(47.65, -122.45));
        Assert.Equal(4, oneLayers.Overlay(SplitMapOverlayLayer.EchoLayerName).Features.Count());

        // The pointer leaves: gone.
        split.Services.CursorEcho.Report(split.Two.Id, null);
        Assert.Empty(oneLayers.Overlay(SplitMapOverlayLayer.EchoLayerName).Features);

        // With one map there is no echo.
        split.Services.CursorEcho.Report(split.Two.Id, new GeoPosition(47.65, -122.45));
        split.Panes.CloseSplit();
        Assert.Empty(oneLayers.Overlay(SplitMapOverlayLayer.EchoLayerName).Features);
    }

    private static SplitMapOverlayController Overlays(FakeLayers layers, Split split, IMapViewCapabilities view) =>
        new(
            layers,
            view.Id,
            split.Views,
            split.Services.CursorEcho,
            split.Panes,
            new StubMeasureOverlayAppearanceProvider(),
            a => a(),
            () => { });
}
