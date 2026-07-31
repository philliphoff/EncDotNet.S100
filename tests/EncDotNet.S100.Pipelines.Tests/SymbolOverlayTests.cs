using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the tiled "B" subsystem's screen-space symbol/sounding
/// overlay.
///
/// <para>
/// The tiled base plane is rasterised at a discrete quad-tree band resolution
/// and then composited scaled by <c>ResolutionForBand(band) / resolution</c>,
/// so any op baked into a tile scales with the band fit (and, transiently, with
/// a coarser fallback band) — point symbols and soundings would visibly grow
/// and shrink through a zoom instead of holding a constant on-screen size.
/// <see cref="S100VectorTileRenderer.PartitionScene"/> moves point symbols and
/// point-anchored text out of the tiled base plane into a live overlay drawn
/// against the real viewport, so they stay scale-stable. These tests pin both
/// the partition contract and the constant-size property the overlay relies on.
/// </para>
/// </summary>
public class SymbolOverlayTests
{
    private static PointPaintOp Point(string id, double lon, double lat) =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            FallbackColor = new RgbaColor(220, 20, 60, 255),
            FallbackScale = 1.0,
        };

    private static TextPaintOp Text(string id, double lon, double lat) =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            Text = "12",
            FontSizePx = 10,
            ForeColor = new RgbaColor(0, 0, 0, 255),
        };

    private static AreaPaintOp Area(string id) =>
        new()
        {
            FeatureReference = id,
            WorldShell = new (double X, double Y)[]
            {
                WebMercator.FromLonLat(0, 0),
                WebMercator.FromLonLat(1, 0),
                WebMercator.FromLonLat(1, 1),
            },
            Fill = new RgbaColor(0, 128, 255, 255),
        };

    private static LinePaintOp Line(string id) =>
        new()
        {
            FeatureReference = id,
            World = new (double X, double Y)[]
            {
                WebMercator.FromLonLat(0, 0),
                WebMercator.FromLonLat(1, 1),
            },
            Color = new RgbaColor(0, 0, 0, 255),
            WidthPx = 1,
        };

    private static PatternAreaPaintOp Pattern(string id) =>
        new()
        {
            FeatureReference = id,
            PatternReference = "PAT",
            WorldShell = new (double X, double Y)[]
            {
                WebMercator.FromLonLat(0, 0),
                WebMercator.FromLonLat(1, 0),
                WebMercator.FromLonLat(1, 1),
            },
            TilePng = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        };

    [Fact]
    public void PartitionScene_RoutesPointAndTextToOverlay_LeavesFillsAndLinesInBase()
    {
        // Interleave op kinds so the test also covers order preservation.
        var ops = new List<PaintOp>
        {
            Area("area"),
            Point("buoy", 1, 1),
            Line("line"),
            Text("sounding", 1, 1),
            Pattern("pat"),
        };

        var (baseScene, overlay) = S100VectorTileRenderer.PartitionScene(new VectorScene(ops));

        // Base keeps area / line / pattern fills, in their original order, and
        // contains no point or text ops (nothing that must stay constant size).
        Assert.Equal(new[] { "area", "line", "pat" },
            baseScene.Ops.Select(op => op.FeatureReference).ToArray());
        Assert.DoesNotContain(baseScene.Ops, op => op is PointPaintOp or TextPaintOp);

        // Overlay holds exactly the point symbol and the sounding text, in order.
        Assert.Equal(new[] { "buoy", "sounding" },
            overlay.Ops.Select(op => op.FeatureReference).ToArray());
        Assert.All(overlay.Ops, op => Assert.True(op is PointPaintOp or TextPaintOp));
    }

    [Fact]
    public void PartitionScene_NoPointsOrText_ProducesEmptyOverlay()
    {
        var (baseScene, overlay) = S100VectorTileRenderer.PartitionScene(
            new VectorScene(new List<PaintOp> { Area("a"), Line("l") }));

        Assert.Equal(2, baseScene.Ops.Count);
        Assert.Empty(overlay.Ops);
    }

    [Fact]
    public void RenderOnto_PointSymbol_HasConstantPixelSizeAcrossZoom()
    {
        const double lon = 10.0;
        const double lat = 0.0;
        var scene = new VectorScene(new List<PaintOp> { Point("buoy", lon, lat) });

        var renderer = new SkiaDisplayListRenderer
        {
            Background = RgbaColor.Transparent,
            HonorScaleVisibility = false,
        };

        // Two viewports centred on the same point but one zoomed in 4× (a
        // quarter of the span). A scale-stable symbol must occupy the same
        // number of pixels in both.
        var zoomedOut = MeasureOpaqueBounds(renderer, scene, Centred(lon, lat, 0.4));
        var zoomedIn = MeasureOpaqueBounds(renderer, scene, Centred(lon, lat, 0.1));

        Assert.True(zoomedOut.Width > 0 && zoomedOut.Height > 0, "symbol did not render");
        Assert.Equal(zoomedOut.Width, zoomedIn.Width);
        Assert.Equal(zoomedOut.Height, zoomedIn.Height);
    }

    [Fact]
    public void RenderOnto_PointAnchoredFarOutsideViewport_IsCulled()
    {
        const double lon = 10.0;
        const double lat = 0.0;
        var renderer = new SkiaDisplayListRenderer
        {
            Background = RgbaColor.Transparent,
            HonorScaleVisibility = false,
        };

        // A point centred in the viewport renders; the same point pushed far
        // off-screen (many viewport widths away) is culled before it draws.
        var inView = MeasureOpaqueBounds(renderer,
            new VectorScene(new List<PaintOp> { Point("p", lon, lat) }),
            Centred(lon, lat, 0.1));
        var offView = MeasureOpaqueBounds(renderer,
            new VectorScene(new List<PaintOp> { Point("p", lon + 5.0, lat) }),
            Centred(lon, lat, 0.1));

        Assert.True(inView.Width > 0 && inView.Height > 0, "in-view point did not render");
        Assert.Equal((0, 0), offView);
    }

    [Fact]
    public void RenderOnto_SvgSymbol_RendersConsistentlyAcrossRepeatedRenders()
    {
        const double lon = 10.0;
        const double lat = 0.0;
        var scene = new VectorScene(new List<PaintOp> { PointWithSvg("buoy", lon, lat) });
        var renderer = new SkiaDisplayListRenderer
        {
            Background = RgbaColor.Transparent,
            HonorScaleVisibility = false,
        };

        // The parsed symbol picture is cached process-wide and reused across
        // renders/frames. A regression that disposed the cached picture would
        // yield a null picture on the next render (falling back to a dot, or
        // nothing) — so repeated renders of the same symbol must stay identical.
        var first = MeasureOpaqueBounds(renderer, scene, Centred(lon, lat, 0.1));
        Assert.True(first.Width > 0 && first.Height > 0, "symbol did not render");
        for (var i = 0; i < 3; i++)
        {
            var again = MeasureOpaqueBounds(renderer, scene, Centred(lon, lat, 0.1));
            Assert.Equal(first, again);
        }
    }

    private static PointPaintOp PointWithSvg(string id, double lon, double lat) =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            Symbol = new ResolvedSymbol(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3.98mm\" " +
                "height=\"3.98mm\" viewBox=\"-1.99 -1.99 3.98 3.98\">" +
                "<rect x=\"-1.99\" y=\"-1.99\" width=\"3.98\" height=\"3.98\" fill=\"red\"/></svg>",
                Scale: 1.0,
                PivotRelativeX: 0.0,
                PivotRelativeY: 0.0),
            FallbackColor = new RgbaColor(220, 20, 60, 255),
            FallbackScale = 1.0,
        };

    private static Viewport Centred(double lon, double lat, double span) =>
        new()
        {
            MinLongitude = lon - span / 2,
            MaxLongitude = lon + span / 2,
            MinLatitude = lat - span / 2,
            MaxLatitude = lat + span / 2,
            WidthPixels = 256,
            HeightPixels = 256,
            ScaleDenominator = 50000,
        };

    private static (int Width, int Height) MeasureOpaqueBounds(
        SkiaDisplayListRenderer renderer, VectorScene scene, Viewport viewport)
    {
        using var bitmap = renderer.Render(scene, viewport);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < 0 ? (0, 0) : (maxX - minX + 1, maxY - minY + 1);
    }
}
