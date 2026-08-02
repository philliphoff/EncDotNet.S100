using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests the issue #427 metatile planner, SCAMIN safety split, and exact
/// tile-granular output.
/// </summary>
public sealed class TileMetatileTests
{
    private const int Band = 10;

    [Fact]
    public void TakeMetatile_ClaimsOnlyAlignedPeersFromPendingTier()
    {
        var seed = new TileKey(Band, 100, 100);
        var peers = new[]
        {
            seed,
            new TileKey(Band, 101, 100),
            new TileKey(Band, 100, 101),
            new TileKey(Band, 101, 101),
        };
        var outside = new TileKey(Band, 102, 100);
        var otherBand = new TileKey(Band + 1, 100, 100);
        var pending = peers.Append(outside).Append(otherBand).ToHashSet();
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(seed);

        var claimed = S100VectorTileRenderer.TakeMetatile(
            pending,
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            enabled: true);

        Assert.Equal(seed, claimed[0]);
        Assert.Equal(peers.ToHashSet(), claimed.ToHashSet());
        Assert.Equal(new HashSet<TileKey> { outside, otherBand }, pending);
    }

    [Fact]
    public void TakeMetatile_DisabledClaimsOnlyNearest()
    {
        var near = new TileKey(Band, 10, 10);
        var peer = new TileKey(Band, 11, 10);
        var pending = new HashSet<TileKey> { near, peer };
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(near);

        var claimed = S100VectorTileRenderer.TakeMetatile(
            pending,
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            enabled: false);

        Assert.Equal([near], claimed);
        Assert.Equal([peer], pending);
    }

    [Fact]
    public void PartitionMetatileForScale_SplitsRowsWhenScaminDiffers()
    {
        var keys = Block();
        var north = TileGrid.TileWorldBounds(keys[0]);
        var south = TileGrid.TileWorldBounds(keys[2]);
        var resolution = TileGrid.ResolutionForBand(Band);
        var northDenominator = S100VectorSceneRenderer.ScaleDenominatorFor(
            (north.MinX + north.MaxX) * 0.5,
            (north.MinY + north.MaxY) * 0.5,
            resolution);
        var southDenominator = S100VectorSceneRenderer.ScaleDenominatorFor(
            (south.MinX + south.MaxX) * 0.5,
            (south.MinY + south.MaxY) * 0.5,
            resolution);
        var threshold = (northDenominator + southDenominator) * 0.5;
        var scene = SceneCovering(keys, scaleMinimum: threshold);

        var groups = S100VectorTileRenderer.PartitionMetatileForScale(
            scene, new BaseSpatialIndex(scene), keys);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Single(group.Select(key => key.Y).Distinct()));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.21f)]
    [InlineData(1.3f)]
    [InlineData(1.5f)]
    public void RasterizeMetatile_SlicesMatchIndependentTiles(float deviceScale)
    {
        var keys = Block();
        var scene = SceneCovering(keys);
        var index = new BaseSpatialIndex(scene);

        var actual = S100VectorTileRenderer.RasterizeMetatile(
            scene, index, keys, deviceScale);
        try
        {
            foreach (var key in keys)
            {
                using var expectedBitmap = S100VectorTileRenderer.RasterizeTile(
                    scene, index, key, deviceScale);
                using var actualBitmap = SKBitmap.FromImage(actual[key]);
                var (different, maxDelta) = PixelDifference(
                    expectedBitmap, actualBitmap);
                var ratio = different /
                    (double)(expectedBitmap.Width * expectedBitmap.Height);

                Assert.True(
                    ratio == 0 && maxDelta == 0,
                    $"{key}: {different} pixels differ ({ratio:P3}), max channel delta {maxDelta}");
            }
        }
        finally
        {
            foreach (var image in actual.Values)
            {
                image.Dispose();
            }
        }
    }

    [SkippableFact]
    public async Task RealS101Metatile_SlicesMatchIndependentTiles()
    {
        var path = ResolveRealCellPath();
        Skip.IfNot(File.Exists(path), $"Real S-101 trial cell not present: {path}");

        var originalSubsystem = RenderingOptimizations.RenderSubsystem;
        IReadOnlyDictionary<TileKey, SKImage>? actual = null;
        try
        {
            RenderingOptimizations.RenderSubsystem = RenderSubsystemKind.TiledScene;
            var processor = CreateFactory().CreateProcessor(path);
            var renderer = new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory());
            var rendered = await renderer.RenderAsync(processor);
            var layer = rendered.Layers.FirstOrDefault(candidate =>
                S100VectorTileRenderer.TryGetPartitionedScene(
                    candidate, out _, out _));
            Assert.NotNull(layer);
            Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(
                layer, out var scene, out _));
            Assert.NotEmpty(scene.Ops);

            const int band = 15;
            var resolution = TileGrid.ResolutionForBand(band);
            var centerX = (rendered.Extent.MinX + rendered.Extent.MaxX) * 0.5;
            var centerY = (rendered.Extent.MinY + rendered.Extent.MaxY) * 0.5;
            var center = TileGrid.VisibleTiles(
                centerX, centerY, 1, 1, resolution, band)[0];
            var xStart = center.X & ~1;
            var yStart = center.Y & ~1;
            IReadOnlyList<TileKey> keys =
            [
                new(band, xStart, yStart),
                new(band, xStart + 1, yStart),
                new(band, xStart, yStart + 1),
                new(band, xStart + 1, yStart + 1),
            ];
            var index = new BaseSpatialIndex(scene);

            actual = S100VectorTileRenderer.RasterizeMetatile(
                scene, index, keys, deviceScale: 1f);
            foreach (var key in keys)
            {
                using var expectedBitmap = S100VectorTileRenderer.RasterizeTile(
                    scene, index, key, deviceScale: 1f);
                using var actualBitmap = SKBitmap.FromImage(actual[key]);
                var (different, maxDelta) = PixelDifference(
                    expectedBitmap, actualBitmap);
                var ratio = different /
                    (double)(expectedBitmap.Width * expectedBitmap.Height);

                Assert.True(
                    ratio <= 0.001 && maxDelta <= 8,
                    $"{key}: {different} pixels differ ({ratio:P3}), max channel delta {maxDelta}");
            }
        }
        finally
        {
            if (actual is not null)
            {
                foreach (var image in actual.Values)
                {
                    image.Dispose();
                }
            }

            RenderingOptimizations.RenderSubsystem = originalSubsystem;
        }
    }

    private static IReadOnlyList<TileKey> Block() =>
    [
        new(Band, 100, 100),
        new(Band, 101, 100),
        new(Band, 100, 101),
        new(Band, 101, 101),
    ];

    private static VectorScene SceneCovering(
        IReadOnlyList<TileKey> keys,
        double? scaleMinimum = null)
    {
        var bounds = keys.Select(TileGrid.TileWorldBounds).ToList();
        PaintOp area = new AreaPaintOp
        {
            FeatureReference = "metatile-area",
            WorldShell =
            [
                (bounds.Min(value => value.MinX), bounds.Min(value => value.MinY)),
                (bounds.Max(value => value.MaxX), bounds.Min(value => value.MinY)),
                (bounds.Max(value => value.MaxX), bounds.Max(value => value.MaxY)),
                (bounds.Min(value => value.MinX), bounds.Max(value => value.MaxY)),
                (bounds.Min(value => value.MinX), bounds.Min(value => value.MinY)),
            ],
            Fill = new RgbaColor(40, 100, 180, 255),
            OutlineColor = new RgbaColor(255, 255, 255, 255),
            OutlineWidthPx = 3,
            ScaleMinimum = scaleMinimum,
        };
        return new VectorScene([area]);
    }

    private static string ResolveRealCellPath()
    {
        var configured = Environment.GetEnvironmentVariable(
            "ENC_DOTNET_PERF_REAL_S101");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Complete S10X datasets", "S-101 Trial Cells",
            "101GB00302045", "101GB00GB302045", "101GB00GB302045.000");
    }

    private static DatasetPipelineFactory CreateFactory()
    {
        var portrayalCatalogues = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                portrayalCatalogues.SetSource(
                    spec, Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        return new DatasetPipelineFactory(
            portrayalCatalogues,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider());
    }

    private static (int Different, int MaxDelta) PixelDifference(
        SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        var different = 0;
        var maxDelta = 0;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var left = expected.GetPixel(x, y);
                var right = actual.GetPixel(x, y);
                if (left == right)
                {
                    continue;
                }

                different++;
                maxDelta = Math.Max(
                    maxDelta,
                    Math.Max(
                        Math.Max(
                            Math.Abs(left.Red - right.Red),
                            Math.Abs(left.Green - right.Green)),
                        Math.Max(
                            Math.Abs(left.Blue - right.Blue),
                            Math.Abs(left.Alpha - right.Alpha))));
            }
        }

        return (different, maxDelta);
    }
}
