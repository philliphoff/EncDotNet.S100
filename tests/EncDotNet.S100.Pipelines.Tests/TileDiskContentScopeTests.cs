using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Specifications;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins that the warm tile disk-cache namespace tells datasets apart by
/// content. The <c>styleStateHash</c> hashes drawing instructions, which do
/// not carry geometry, so two same-shaped datasets of one product (133 S-421
/// SECOM routes with the same feature ids and leg counts) used to share a
/// namespace and be served each other's tiles: route legs went missing at tile
/// edges, leaving orphan waypoint circles.
/// </summary>
public class TileDiskContentScopeTests : IDisposable
{
    private const string Fixture = "navwarn_drawing_parts.gml";
    private const string OriginalLeg = "-16.60 145.80 -16.55 145.90";
    private const string MovedLeg = "-16.60 145.85 -16.55 145.90";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "encdotnet-s100-tile-scope-tests", Path.GetRandomFileName());

    public TileDiskContentScopeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task Same_instructions_different_geometry_get_different_namespaces_and_do_not_share_tiles()
    {
        var original = await Portray(WriteFixture("original.gml", moveLeg: false));
        var moved = await Portray(WriteFixture("moved.gml", moveLeg: true));

        // The case the instruction hash alone cannot tell apart.
        Assert.Equal(
            DrawingInstructionSerializer.Serialize(Instructions(original)),
            DrawingInstructionSerializer.Serialize(Instructions(moved)));
        Assert.NotNull(original.ContentScope);
        Assert.NotEqual(original.ContentScope, moved.ContentScope);

        var originalNs = Namespace(original);
        var movedNs = Namespace(moved);
        Assert.NotEqual(originalNs, movedNs);

        using var cache = new TileDiskCache(Path.Combine(_root, "tiles"), 64L * 1024 * 1024);
        var key = new TileKey(10, 3, 4);
        using (var image = SolidImage(64))
            cache.Write(originalNs, key, image);

        using (var hit = cache.TryRead(originalNs, key))
            Assert.NotNull(hit);
        Assert.Null(cache.TryRead(movedNs, key));
    }

    [Fact]
    public async Task Same_bytes_reloaded_keep_their_namespace()
    {
        // Warm reuse across layer rebuilds and sessions still works.
        var first = await Portray(WriteFixture("a.gml", moveLeg: false));
        var second = await Portray(WriteFixture("b.gml", moveLeg: false));

        Assert.Equal(first.ContentScope, second.ContentScope);
        Assert.Equal(Namespace(first), Namespace(second));
    }

    [Fact]
    public void Without_a_content_scope_the_layer_bypasses_the_disk_cache()
    {
        var renderer = new MapsuiDisplayListRenderer { Product = "S-421" };

        Assert.Equal((null, null), renderer.DiskCacheKey([]));
    }

    [Fact]
    public void HashAndRewind_buffers_a_forward_only_stream_and_hashes_the_same_bytes()
    {
        var bytes = new byte[100_000];
        new Random(7).NextBytes(bytes);

        Stream seekable = new MemoryStream(bytes);
        Stream forwardOnly = new ForwardOnlyStream(bytes);

        var a = DatasetContentHash.HashAndRewind(ref seekable);
        var b = DatasetContentHash.HashAndRewind(ref forwardOnly);

        Assert.Equal(a, b);
        Assert.True(forwardOnly.CanSeek);
        Assert.Equal(0, forwardOnly.Position);
        Assert.Equal(0, seekable.Position);
    }

    private string WriteFixture(string name, bool moveLeg)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "S124", Fixture));
        Assert.Contains(OriginalLeg, text);
        if (moveLeg)
            text = text.Replace(OriginalLeg, MovedLeg, StringComparison.Ordinal);

        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static async Task<Datasets.Pipelines.Portrayal.VectorPortrayalResult> Portray(string path)
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        var processor = new S124DatasetProcessor(
            path,
            manager,
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

        return await processor.BuildVectorPortrayalAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    private static List<DrawingInstruction> Instructions(Datasets.Pipelines.Portrayal.VectorPortrayalResult result) =>
        result.SubLayers.SelectMany(l => l.Instructions).ToList();

    /// <summary>The per-pixel-size disk namespace the renderer would bind this result's layer under.</summary>
    private static string Namespace(Datasets.Pipelines.Portrayal.VectorPortrayalResult result)
    {
        var renderer = new MapsuiDisplayListRenderer
        {
            Product = result.Product,
            ContentScope = result.ContentScope,
            Palette = result.Palette,
        };

        var (productLayerSet, styleStateHash) = renderer.DiskCacheKey(Instructions(result));
        Assert.NotNull(productLayerSet);
        Assert.NotNull(styleStateHash);
        return TileDiskCache.NamespaceFor(TileDiskCache.NamespaceFor(productLayerSet, styleStateHash), 64);
    }

    private static SKImage SolidImage(int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Magenta);
        return surface.Snapshot();
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
