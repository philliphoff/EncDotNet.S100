using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

public class AvaloniaMapsuiMapAdapterTests
{
    [AvaloniaFact]
    public async Task Attach_requires_ui_thread()
    {
        // Run inside the headless session so a real Avalonia UI thread exists,
        // then dispatch Attach from a genuine non-UI (thread pool) thread. This
        // keeps the thread-affinity check deterministic regardless of whether a
        // prior test already bound the dispatcher.
        var control = new CaptureSynchronizedMapControl { Map = new Map() };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => AvaloniaMapsuiMapAdapter.Attach(control)));
    }

    [AvaloniaFact]
    public void Converts_live_viewport_center_to_wgs84()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        var position = adapter.TryScreenToWgs84(400, 300);

        Assert.NotNull(position);
        Assert.Equal(10.0, position.Value.Latitude, 6);
        Assert.Equal(20.0, position.Value.Longitude, 6);
        Assert.Equal((800.0, 600.0), adapter.TryGetViewportSizePx());
    }

    [AvaloniaFact]
    public void Snapshot_pixel_conversion_matches_rotated_snapshot_viewport()
    {
        var map = CreateLaidOutMap();
        map.Navigator.RotateTo(35, duration: 0);
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        var captured = adapter.TryImagePixelToWgs84(250, 180, 800, 600);
        using var expectedMap = new Map();
        expectedMap.Navigator.SetSize(800, 600);
        expectedMap.Navigator.ZoomToBox(
            map.Navigator.Viewport.ToExtent()!,
            MBoxFit.Fit);
        expectedMap.Navigator.RotateTo(35, duration: 0);
        var expectedWorld = expectedMap.Navigator.Viewport.ScreenToWorld(250, 180);
        var (expectedLongitude, expectedLatitude) = SphericalMercator.ToLonLat(
            expectedWorld.X,
            expectedWorld.Y);

        Assert.NotNull(captured);
        Assert.Equal(expectedLatitude, captured.Value.Latitude, 6);
        Assert.Equal(expectedLongitude, captured.Value.Longitude, 6);
    }

    [AvaloniaFact]
    public void Coordinate_conversion_rejects_unsupported_map_crs()
    {
        var map = CreateLaidOutMap();
        map.CRS = "EPSG:32632";
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        Assert.Null(adapter.TryScreenToWgs84(400, 300));
        Assert.Null(adapter.TryImagePixelToWgs84(400, 300, 800, 600));
    }

    [AvaloniaFact]
    public void Dispose_detaches_without_disposing_borrowed_map()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        adapter.Dispose();
        adapter.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => adapter.TryGetViewportSizePx());
        Assert.Same(map, control.Map);
    }

    [AvaloniaFact]
    public void RequestRedraw_raises_map_refresh_request()
    {
        var map = CreateLaidOutMap();
        var requested = false;
        map.RefreshGraphicsRequest += (_, _) => requested = true;
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        adapter.RequestRedraw();

        Assert.True(requested);
    }

    [AvaloniaFact]
    public void Adapter_attaches_to_a_plain_map_control()
    {
        // Map.AddS100 / Attach accept any Mapsui MapControl: a host that never
        // captures the view to an image needs no capture-synchronized subclass.
        // The adapter is fully functional over a stock control - redraw and
        // coordinate conversion behave identically.
        var map = CreateLaidOutMap();
        var requested = false;
        map.RefreshGraphicsRequest += (_, _) => requested = true;
        var control = new global::Mapsui.UI.Avalonia.MapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        adapter.RequestRedraw();
        var position = adapter.TryScreenToWgs84(400, 300);

        Assert.True(requested);
        Assert.NotNull(position);
        Assert.Equal(10.0, position.Value.Latitude, 6);
        Assert.Equal(20.0, position.Value.Longitude, 6);
        Assert.Equal((800.0, 600.0), adapter.TryGetViewportSizePx());
    }

    [Fact]
    public void Attach_and_AddS100_accept_a_plain_map_control_type()
    {
        // Lock the widened contract: neither entry point requires the
        // capture-synchronized subclass, so the base Mapsui control binds.
        var attachParam = typeof(AvaloniaMapsuiMapAdapter)
            .GetMethod(nameof(AvaloniaMapsuiMapAdapter.Attach))!
            .GetParameters()[0]
            .ParameterType;
        var addS100Param = typeof(AvaloniaS100MapExtensions)
            .GetMethod(nameof(AvaloniaS100MapExtensions.AddS100))!
            .GetParameters()[0]
            .ParameterType;

        Assert.Equal(typeof(global::Mapsui.UI.Avalonia.MapControl), attachParam);
        Assert.Equal(typeof(global::Mapsui.UI.Avalonia.MapControl), addS100Param);
    }

    [AvaloniaFact]
    public async Task Render_current_view_returns_png()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var png = await adapter.RenderCurrentViewToPngAsync(320, 200, 1.0);

        Assert.NotNull(png);
        Assert.True(png.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
    }

    [AvaloniaFact]
    public void Control_capture_returns_null_for_unlaid_out_target()
    {
        var png = AvaloniaControlCapture.CapturePngAsync(
            new global::Mapsui.UI.Avalonia.MapControl())
            .GetAwaiter()
            .GetResult();
        Assert.Null(png);
    }

    [AvaloniaFact]
    public async Task Plain_control_capture_bypasses_mapsui_synchronization()
    {
        var target = new CaptureProbeControl();
        target.Measure(new Size(20, 10));
        target.Arrange(new Rect(0, 0, 20, 10));

        Assert.False(
            AvaloniaControlCapture.RequiresCaptureSynchronization(target));
        var png = await AvaloniaControlCapture.CapturePngAsync(target);

        Assert.NotNull(png);
        Assert.False(target.CaptureWasActive);
    }

    [AvaloniaFact]
    public async Task Capture_at_scale_two_renders_the_control()
    {
        var target = new CaptureProbeControl();
        target.Measure(new Size(20, 10));
        target.Arrange(new Rect(0, 0, 20, 10));
        var png = await AvaloniaControlCapture.CapturePngAsync(target, 2.0);

        // Headless drawing is a stub, so pixel sizes are checked in the live viewer;
        // this covers the scaled render path end to end.
        Assert.NotNull(png);
    }

    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public async Task Capture_rejects_a_non_positive_scale(double scale)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => AvaloniaControlCapture.CapturePngAsync(new Border(), scale));
    }

    [AvaloniaFact]
    public async Task Plain_mapsui_control_tree_capture_is_rejected()
    {
        var target = new Border
        {
            Child = new global::Mapsui.UI.Avalonia.MapControl(),
        };
        target.Measure(new Size(20, 10));
        target.Arrange(new Rect(0, 0, 20, 10));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AvaloniaControlCapture.CapturePngAsync(target));
    }

    [Fact]
    public void Base_mapsui_renderer_remains_avalonia_free()
    {
        var references = typeof(EncDotNet.S100.Renderers.Mapsui.MapsuiLayerBands)
            .Assembly
            .GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.Contains(
                "Avalonia",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Snapshot_copies_map_state_without_borrowing_layers()
    {
        var liveMap = CreateLaidOutMap();
        liveMap.CRS = "EPSG:32632";
        liveMap.BackColor = global::Mapsui.Styles.Color.Red;
        liveMap.Layers.Add(new global::Mapsui.Layers.MemoryLayer());

        Assert.True(
            AvaloniaMapsuiMapAdapter.TryConfigureSnapshotMap(
                liveMap,
                320,
                200,
                out var snapshot));

        using (snapshot)
        {
            Assert.Equal(liveMap.CRS, snapshot.CRS);
            Assert.Equal(liveMap.BackColor, snapshot.BackColor);
            Assert.Equal(0, snapshot.Layers.Count);
        }
    }

    [Fact]
    public void Snapshot_does_not_allocate_map_for_unlaid_out_viewport()
    {
        using var liveMap = new Map();

        Assert.False(
            AvaloniaMapsuiMapAdapter.TryConfigureSnapshotMap(
                liveMap,
                320,
                200,
                out var snapshot));
        Assert.Null(snapshot);
    }

    [AvaloniaFact]
    public void PickAtScreen_translates_pixel_to_geographic_query()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var recording = new RecordingMapQuery();

        var picks = adapter
            .PickAtScreenAsync(recording, 400, 300, radiusMeters: 25, maxResults: 3)
            .GetAwaiter()
            .GetResult();

        // The pick is delegated to the session query unchanged.
        Assert.Same(recording.Result, picks);
        Assert.Equal(1, recording.CallCount);

        // The center pixel maps to the map center (lon 20 / lat 10), the live
        // resolution (100) rides along for scale filtering, and the tolerance
        // and cap pass through.
        var query = Assert.IsType<GeographicPickQuery>(recording.LastQuery);
        Assert.Equal(10.0, query.Latitude, 6);
        Assert.Equal(20.0, query.Longitude, 6);
        Assert.Equal(100.0, query.Resolution!.Value, 6);
        Assert.Equal(25.0, query.RadiusMeters, 6);
        Assert.Equal(3, query.MaxResults);
    }

    [AvaloniaFact]
    public void PickAtScreen_returns_empty_without_querying_for_unsupported_crs()
    {
        var map = CreateLaidOutMap();
        map.CRS = "EPSG:32632";
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var recording = new RecordingMapQuery();

        var picks = adapter
            .PickAtScreenAsync(recording, 400, 300)
            .GetAwaiter()
            .GetResult();

        Assert.Empty(picks);
        Assert.Equal(0, recording.CallCount);
    }

    [AvaloniaFact]
    public void PickAtScreen_returns_empty_without_querying_for_unlaid_out_viewport()
    {
        // A map with no laid-out viewport (never sized) has width/height 0.
        using var map = new Map();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var recording = new RecordingMapQuery();

        var picks = adapter
            .PickAtScreenAsync(recording, 400, 300)
            .GetAwaiter()
            .GetResult();

        Assert.Empty(picks);
        Assert.Equal(0, recording.CallCount);
    }

    [AvaloniaFact]
    public void PickAtScreen_returns_empty_without_querying_for_non_finite_pixel()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var recording = new RecordingMapQuery();

        var picks = adapter
            .PickAtScreenAsync(recording, double.NaN, 300)
            .GetAwaiter()
            .GetResult();

        Assert.Empty(picks);
        Assert.Equal(0, recording.CallCount);
    }

    [AvaloniaFact]
    public async Task PickAtScreen_runs_query_off_the_ui_thread()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var recording = new ThreadRecordingMapQuery();

        // On the UI thread. The query must be dispatched to the thread pool
        // so a session using ConfigureAwait(true) can't post its continuation
        // back to a (potentially blocked) UI thread.
        await adapter.PickAtScreenAsync(recording, 400, 300);

        // Non-null confirms the query actually ran; false confirms it ran
        // off the UI thread.
        Assert.NotNull(recording.InvokedWithUiAccess);
        Assert.False(recording.InvokedWithUiAccess.Value);
    }

    [AvaloniaFact]
    public async Task PickAtScreen_queries_directly_when_called_off_the_ui_thread()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
        var recording = new RecordingMapQuery();

        // Called from a pool thread: the adapter hops to the UI thread only
        // to read the viewport, then queries directly (no redundant Task.Run).
        var picks = await Task.Run(
            () => adapter.PickAtScreenAsync(recording, 400, 300));

        Assert.Same(recording.Result, picks);
        Assert.Equal(1, recording.CallCount);
    }

    [AvaloniaFact]
    public async Task PickAtScreen_rejects_null_query()
    {
        var map = CreateLaidOutMap();
        var control = new CaptureSynchronizedMapControl { Map = map };
        using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.PickAtScreenAsync(null!, 400, 300));
    }

    private sealed class RecordingMapQuery : IS100MapQuery
    {
        public GeographicPickQuery? LastQuery { get; private set; }

        public int CallCount { get; private set; }

        public IReadOnlyList<S100Pick> Result { get; } = new List<S100Pick>();

        public Task<IReadOnlyList<S100Pick>> PickAsync(
            GeographicPickQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class ThreadRecordingMapQuery : IS100MapQuery
    {
        public bool? InvokedWithUiAccess { get; private set; }

        public IReadOnlyList<S100Pick> Result { get; } = new List<S100Pick>();

        public Task<IReadOnlyList<S100Pick>> PickAsync(
            GeographicPickQuery query,
            CancellationToken cancellationToken = default)
        {
            InvokedWithUiAccess = Dispatcher.UIThread.CheckAccess();
            return Task.FromResult(Result);
        }
    }

    private static Map CreateLaidOutMap()
    {
        var map = new Map();
        var (x, y) = SphericalMercator.FromLonLat(20, 10);
        map.Navigator.SetSize(800, 600);
        map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution: 100, duration: 0);
        return map;
    }

    private sealed class CaptureProbeControl : Control
    {
        public bool CaptureWasActive { get; private set; }

        public override void Render(DrawingContext context)
        {
            CaptureWasActive = CaptureCoordinator.CaptureActive;
            base.Render(context);
        }
    }
}
