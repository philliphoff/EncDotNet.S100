using System.Diagnostics;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins that the warm tile disk-cache namespace folds in the bound scene's
/// geometry. The <c>styleStateHash</c> hashes drawing instructions, which do
/// not carry geometry, so two same-shaped datasets of one product (133 S-421
/// SECOM routes with the same feature ids and leg counts) used to share a
/// namespace and be served each other's tiles: route legs went missing at tile
/// edges, leaving orphan waypoint circles.
/// </summary>
public class TileDiskNamespaceSceneTests : IDisposable
{
    // Above the live-base vertex limit (4096), so the scene is portrayed by
    // tiles even when sparse scenes are drawn live.
    private const int VerticesPerScene = 8192;

    private const string Product = "S-421";
    private const string StyleStateHash = "identical-instructions";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "encdotnet-s100-tile-ns-tests", Path.GetRandomFileName());

    private readonly ITestOutputHelper _output;

    public TileDiskNamespaceSceneTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void SameInstructions_DifferentGeometry_GetDifferentNamespaces_AndDoNotShareTiles()
    {
        var first = RouteScene(offset: 0);
        var second = RouteScene(offset: 2_000);

        var firstNs = S100VectorTileRenderer.DiskNamespaceFor(first, Product, StyleStateHash);
        var secondNs = S100VectorTileRenderer.DiskNamespaceFor(second, Product, StyleStateHash);

        Assert.NotNull(firstNs);
        Assert.NotNull(secondNs);
        Assert.NotEqual(firstNs, secondNs);

        using var cache = new TileDiskCache(_root, 64L * 1024 * 1024);
        var key = new TileKey(10, 3, 4);
        using (var image = SolidImage(64))
        {
            cache.Write(TileDiskCache.NamespaceFor(firstNs, 64), key, image);
        }

        using (var firstHit = cache.TryRead(TileDiskCache.NamespaceFor(firstNs, 64), key))
            Assert.NotNull(firstHit);

        Assert.Null(cache.TryRead(TileDiskCache.NamespaceFor(secondNs, 64), key));
    }

    [Fact]
    public void SameGeometry_RebuiltScene_KeepsItsNamespace()
    {
        // Warm reuse across layer rebuilds and sessions still works: an equal
        // scene, rebuilt from scratch, maps to the same namespace.
        var a = S100VectorTileRenderer.DiskNamespaceFor(RouteScene(offset: 0), Product, StyleStateHash);
        var b = S100VectorTileRenderer.DiskNamespaceFor(RouteScene(offset: 0), Product, StyleStateHash);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ChangedLabelText_ChangesNamespace()
    {
        static VectorScene Labelled(string text) => new(new List<PaintOp>
        {
            new TextPaintOp { FeatureReference = "wp1", World = (10, 20), Text = text },
        });

        Assert.NotEqual(
            S100VectorTileRenderer.DiskNamespaceFor(Labelled("WP 1"), Product, StyleStateHash),
            S100VectorTileRenderer.DiskNamespaceFor(Labelled("WP 2"), Product, StyleStateHash));
    }

    [Fact]
    public void MissingKey_BypassesDiskCache()
    {
        var scene = RouteScene(offset: 0);

        Assert.Null(S100VectorTileRenderer.DiskNamespaceFor(scene, null, StyleStateHash));
        Assert.Null(S100VectorTileRenderer.DiskNamespaceFor(scene, Product, null));
    }

    [Fact]
    public void Fingerprint_LargeCell_IsCheapToBind()
    {
        // Roughly a dense S-101 cell's vertex count, spread over many ops.
        var ops = new List<PaintOp>();
        for (var i = 0; i < 20_000; i++)
        {
            var ring = new (double X, double Y)[50];
            for (var v = 0; v < ring.Length; v++)
                ring[v] = (i * 10.0 + v, i * 7.0 - v);

            ops.Add(new AreaPaintOp { FeatureReference = $"f{i}", WorldShell = ring });
        }
        var scene = new VectorScene(ops);

        VectorSceneFingerprint.Compute(scene); // warm up
        var sw = Stopwatch.StartNew();
        VectorSceneFingerprint.Compute(scene);
        sw.Stop();

        _output.WriteLine($"Fingerprint of {ops.Count:N0} ops / 1,000,000 vertices: {sw.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"fingerprint took {sw.Elapsed.TotalMilliseconds:F1} ms");
    }

    /// <summary>
    /// A route scene: one leg polyline and a waypoint symbol per vertex, with
    /// identical feature references across offsets so only geometry differs.
    /// </summary>
    private static VectorScene RouteScene(double offset)
    {
        var leg = new List<(double X, double Y)>(VerticesPerScene);
        for (var i = 0; i < VerticesPerScene; i++)
            leg.Add((offset + i * 100.0, offset + (i % 2) * 50.0));

        return new VectorScene(new List<PaintOp>
        {
            new LinePaintOp
            {
                FeatureReference = "leg",
                World = leg,
                Color = new RgbaColor(200, 0, 200, 255),
                WidthPx = 2,
            },
            new PointPaintOp { FeatureReference = "wp1", World = leg[0] },
            new PointPaintOp { FeatureReference = "wp2", World = leg[^1] },
        });
    }

    private static SKImage SolidImage(int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Magenta);
        return surface.Snapshot();
    }
}
