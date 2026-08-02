using Avalonia.Controls;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

public class AvaloniaMapsuiMapAdapterTests
{
    [Fact]
    public async Task Attach_requires_ui_thread()
    {
        var control = new CaptureSynchronizedMapControl { Map = new Map() };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.Run(() => AvaloniaMapsuiMapAdapter.Attach(control)));
    }

    [Fact]
    public void Converts_live_viewport_center_to_wgs84()
    {
        HeadlessTest.Run(() =>
        {
            var map = CreateLaidOutMap();
            var control = new CaptureSynchronizedMapControl { Map = map };
            using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

            var position = adapter.TryScreenToWgs84(400, 300);

            Assert.NotNull(position);
            Assert.Equal(10.0, position.Value.Latitude, 6);
            Assert.Equal(20.0, position.Value.Longitude, 6);
            Assert.Equal((800.0, 600.0), adapter.TryGetViewportSizePx());
        });
    }

    [Fact]
    public void Snapshot_pixel_conversion_matches_rotated_snapshot_viewport()
    {
        HeadlessTest.Run(() =>
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
        });
    }

    [Fact]
    public void Coordinate_conversion_rejects_unsupported_map_crs()
    {
        HeadlessTest.Run(() =>
        {
            var map = CreateLaidOutMap();
            map.CRS = "EPSG:32632";
            var control = new CaptureSynchronizedMapControl { Map = map };
            using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

            Assert.Null(adapter.TryScreenToWgs84(400, 300));
            Assert.Null(adapter.TryImagePixelToWgs84(400, 300, 800, 600));
        });
    }

    [Fact]
    public void Dispose_detaches_without_disposing_borrowed_map()
    {
        HeadlessTest.Run(() =>
        {
            var map = CreateLaidOutMap();
            var control = new CaptureSynchronizedMapControl { Map = map };
            var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

            adapter.Dispose();
            adapter.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => adapter.TryGetViewportSizePx());
            Assert.Same(map, control.Map);
        });
    }

    [Fact]
    public void RequestRedraw_raises_map_refresh_request()
    {
        HeadlessTest.Run(() =>
        {
            var map = CreateLaidOutMap();
            var requested = false;
            map.RefreshGraphicsRequest += (_, _) => requested = true;
            var control = new CaptureSynchronizedMapControl { Map = map };
            using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);

            adapter.RequestRedraw();

            Assert.True(requested);
        });
    }

    [Fact]
    public async Task Render_current_view_returns_png()
    {
        var png = await HeadlessTest.RunAsync(async () =>
        {
            var map = CreateLaidOutMap();
            var control = new CaptureSynchronizedMapControl { Map = map };
            using var adapter = AvaloniaMapsuiMapAdapter.Attach(control);
            return await adapter.RenderCurrentViewToPngAsync(320, 200, 1.0);
        });

        Assert.NotNull(png);
        Assert.True(png.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
    }

    [Fact]
    public void Control_capture_returns_null_for_unlaid_out_target()
    {
        byte[]? png = [];
        HeadlessTest.Run(() =>
        {
            png = AvaloniaControlCapture.CapturePngAsync(new Border())
                .GetAwaiter()
                .GetResult();
        });
        Assert.Null(png);
        Assert.Null(png);
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

    private static Map CreateLaidOutMap()
    {
        var map = new Map();
        var (x, y) = SphericalMercator.FromLonLat(20, 10);
        map.Navigator.SetSize(800, 600);
        map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution: 100, duration: 0);
        return map;
    }
}
