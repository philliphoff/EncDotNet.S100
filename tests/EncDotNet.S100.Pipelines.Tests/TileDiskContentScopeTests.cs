using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Testing.Scenarios.Hdf5;
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

    [Fact]
    public async Task S104_station_tiles_are_keyed_by_the_dataset_and_the_style_version()
    {
        var path = WriteS104Stations("stations.h5");
        var stations = await S104Stations(path);

        // ds:{hash of the bytes}|{the S-104 style's scope}, so the gauges use
        // the disk cache like a vector product.
        Assert.NotNull(stations.Scope);
        Assert.StartsWith("ds:", stations.Scope, StringComparison.Ordinal);
        Assert.EndsWith("|" + S104CoverageStyle.ContentScope, stations.Scope, StringComparison.Ordinal);

        // The same bytes reloaded keep their namespace.
        var reloaded = await S104Stations(WriteS104Stations("copy.h5"));
        Assert.Equal(stations.Scope, reloaded.Scope);
        Assert.Equal(S104Namespace(stations.Layer, stations.Scope), S104Namespace(reloaded.Layer, reloaded.Scope));

        // A new style version, with the same instructions and dataset, gets a
        // namespace of its own, so a tile drawn with the old style isn't read.
        var bumpedScope = stations.Scope.Replace(
            S104CoverageStyle.ContentScope,
            ApplicationContentScope.For("S-104", S104CoverageStyle.StyleVersion + 1),
            StringComparison.Ordinal);
        Assert.NotEqual(stations.Scope, bumpedScope);

        var current = S104Namespace(stations.Layer, stations.Scope);
        var bumped = S104Namespace(stations.Layer, bumpedScope);
        Assert.NotEqual(current, bumped);

        using var cache = new TileDiskCache(Path.Combine(_root, "tiles"), 64L * 1024 * 1024);
        var key = new TileKey(12, 2101, 1350);
        using (var image = SolidImage(64))
            cache.Write(current, key, image);

        using (var hit = cache.TryRead(current, key))
            Assert.NotNull(hit);
        Assert.Null(cache.TryRead(bumped, key));
    }

    private string WriteS104Stations(string name)
    {
        var path = Path.Combine(_root, name);
        S104Dcf8FixtureBuilder.WriteFile(
            path,
            new[]
            {
                S104Station("Rising", 51.90f, 4.10f, 1.42f, 2),
                S104Station("Steady", 51.88f, 4.16f, -0.12f, 3),
            },
            waterLevelTrendThreshold: 0.05);
        return path;
    }

    private static S104Dcf8FixtureBuilder.Station<S104Dcf8FixtureBuilder.SpecValueRow> S104Station(
        string id, float lat, float lon, float height, byte trend) => new()
        {
            Identifier = id,
            Latitude = lat,
            Longitude = lon,
            StartDateTime = "20261014T100000Z",
            EndDateTime = "20261014T100000Z",
            TimeRecordInterval = 3600,
            Values = [new() { WaterLevelHeight = height, WaterLevelTrend = trend }],
        };

    private static async Task<(VectorCoverageSubLayer Layer, string? Scope)> S104Stations(string path)
    {
        var processor = new S104DatasetProcessor(path, new ProjNetCrsTransformFactory());
        var result = await processor.BuildCoveragePortrayalAsync(cancellationToken: TestContext.Current.CancellationToken);
        return (result.SubLayers.OfType<VectorCoverageSubLayer>().Single(), result.ContentScope);
    }

    /// <summary>The per-pixel-size disk namespace the renderer would bind the S-104 station layer under.</summary>
    private static string S104Namespace(VectorCoverageSubLayer layer, string? scope)
    {
        var renderer = new MapsuiDisplayListRenderer
        {
            Product = layer.Product,
            ContentScope = scope,
            Palette = layer.Palette,
        };

        var (productLayerSet, styleStateHash) = renderer.DiskCacheKey(layer.Instructions);
        Assert.NotNull(productLayerSet);
        Assert.NotNull(styleStateHash);
        return TileDiskCache.NamespaceFor(TileDiskCache.NamespaceFor(productLayerSet, styleStateHash), 64);
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
