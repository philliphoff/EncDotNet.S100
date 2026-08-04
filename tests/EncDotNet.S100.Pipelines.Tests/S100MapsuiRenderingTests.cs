using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

public class S100MapsuiRenderingTests
{
    [Fact]
    public void Register_CanBeCalledRepeatedly()
    {
        S100MapsuiRendering.Register();
        S100MapsuiRendering.Register();
    }

    [Fact]
    public void MapsuiDatasetRenderer_PreservesLegacyConstructor()
    {
        var constructor = typeof(MapsuiDatasetRenderer).GetConstructor(
            [typeof(ICrsTransformFactory), typeof(IPatternClipCache)]);

        Assert.NotNull(constructor);
    }

    [Theory]
    [InlineData(VectorSceneMode.Single, S100VectorSceneRenderer.RendererName)]
    [InlineData(VectorSceneMode.Tiled, S100VectorTileRenderer.RendererName)]
    public async Task MapsuiDatasetRenderer_UsesCapturedRenderingOptions(
        VectorSceneMode sceneMode,
        string expectedRendererName)
    {
        var options = new S100MapsuiOptions
        {
            RenderSubsystem = RenderSubsystemKind.TiledScene,
            SceneMode = sceneMode,
        };
        var renderer = new MapsuiDatasetRenderer(
            new IdentityCrsTransformFactory(),
            patternClipCache: null,
            options: options);

        var result = await renderer.RenderAsync(new StubVectorProcessor());

        var layer = Assert.Single(result.Layers);
        Assert.Equal(expectedRendererName, layer.CustomLayerRendererName);
    }

    [Fact]
    public async Task MapsuiDatasetRenderer_ReusesFallbackPatternClipCache()
    {
        var options = new S100MapsuiOptions
        {
            RenderSubsystem = RenderSubsystemKind.TiledScene,
            SceneMode = VectorSceneMode.Tiled,
        };
        var renderer = new MapsuiDatasetRenderer(
            new IdentityCrsTransformFactory(),
            patternClipCache: null,
            options);
        var processor = new StubPatternVectorProcessor();

        await renderer.RenderAsync(processor);
        await renderer.RenderAsync(processor);

        Assert.Equal(1, renderer.PatternClipCacheMisses);
        Assert.True(renderer.PatternClipCacheHits >= 1);
    }

    [Fact]
    public void MapsuiDatasetResult_IsOwnedByMapsuiRenderer()
    {
        var assembly = typeof(MapsuiDatasetResult).Assembly;

        Assert.Equal(
            "EncDotNet.S100.Renderers.Mapsui",
            typeof(MapsuiDatasetResult).Namespace);
        Assert.Same(
            typeof(MapsuiDatasetRenderer).Assembly,
            assembly);
        Assert.Null(
            assembly.GetType("EncDotNet.S100.Datasets.Pipelines.DatasetResult"));
    }

    [Fact]
    public async Task MapsuiDatasetRenderer_ConvertsProcessorWithoutViewerOrAvalonia()
    {
        S100MapsuiRendering.Register();
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());

        var result = await renderer.RenderAsync(new StubVectorProcessor());

        var layer = Assert.Single(result.Layers);
        Assert.Equal("Test layer", layer.Name);
        Assert.NotNull(layer.Extent);
    }

    [Fact]
    public void RendererAssembly_DoesNotReferenceViewerOrAvalonia()
    {
        var references = typeof(S100MapsuiRendering).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToArray();

        Assert.DoesNotContain(
            references,
            name => name.StartsWith("EncDotNet.S100.Viewer", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    private sealed class IdentityCrsTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) =>
            IdentityCrsTransform.Instance;
    }

    private sealed class StubVectorProcessor : IDatasetProcessor, IVectorPortrayalSource
    {
        public SpecRef Spec { get; } = new("S-101", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subLayer = new VectorSubLayer
            {
                LayerKey = "test.area",
                LayerName = "Test layer",
                Instructions =
                [
                    new AreaInstruction
                    {
                        FeatureReference = StubGeometryProvider.FeatureReference,
                        FillColor = "TEST_FILL",
                    },
                ],
                Plane = S98DisplayPlane.BaseChartUnder,
            };

            var result = new VectorPortrayalResult
            {
                SubLayers = [subLayer],
                Palette = new ColorPalette(
                    "Test",
                    new Dictionary<string, string>
                    {
                        ["TEST_FILL"] = "#336699",
                    }),
                GeometryProvider = new StubGeometryProvider(),
                Product = "S-101",
                Spec = Spec,
                SourceDatasetId = "test-dataset",
                Info = "Test dataset",
            };

            return Task.FromResult(result);
        }
    }

    private sealed class StubGeometryProvider : IFeatureGeometryProvider
    {
        public const string FeatureReference = "feature-1";

        public FeatureGeometry? GetGeometry(string featureReference) =>
            featureReference == FeatureReference
                ? new FeatureGeometry
                {
                    Type = GeometryType.Surface,
                    Coordinates =
                    [
                        new GeoPosition(0.0, 0.0),
                        new GeoPosition(0.0, 1.0),
                        new GeoPosition(1.0, 1.0),
                        new GeoPosition(1.0, 0.0),
                        new GeoPosition(0.0, 0.0),
                    ],
                }
                : null;
    }

    private sealed class StubPatternVectorProcessor : IDatasetProcessor, IVectorPortrayalSource
    {
        public SpecRef Spec { get; } = new("S-101", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var subLayer = new VectorSubLayer
            {
                LayerKey = "test.pattern",
                LayerName = "Test pattern layer",
                Instructions =
                [
                    new AreaInstruction
                    {
                        FeatureReference = StubGeometryProvider.FeatureReference,
                        AreaFillReference = "TEST_PATTERN",
                    },
                ],
                PatternClipCacheKey = "test-pattern-key",
                Plane = S98DisplayPlane.BaseChartUnder,
            };

            var result = new VectorPortrayalResult
            {
                SubLayers = [subLayer],
                Palette = ColorPalette.Default,
                GeometryProvider = new StubGeometryProvider(),
                Product = "S-101",
                Spec = Spec,
                SourceDatasetId = "test-pattern-dataset",
                Info = "Test pattern dataset",
                AreaFillProvider = static name => new AreaFill
                {
                    Name = name,
                    PatternSymbol = name,
                    V1X = 4.0,
                    V1Y = 0.0,
                    V2X = 0.0,
                    V2Y = 4.0,
                },
                SymbolProvider = static _ =>
                    """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 4 4"><rect width="4" height="4" fill="black"/></svg>""",
            };

            return Task.FromResult(result);
        }
    }
}
