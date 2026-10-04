using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins <see cref="LayerStackProjector.Project"/>'s coverage-rebuild branch:
/// when an S-98 rule replaces a grid sub-layer (e.g. R-101-104-B attaching a
/// land mask so the S-104 surface is clipped to water, issue #483) the rebuilt
/// <see cref="ILayer"/> must inherit the prebuilt layer's display state so a
/// rule-triggered rebuild never re-shows a hidden surface or resets
/// opacity / scale-window choices.
/// </summary>
public class LayerStackProjectorTests
{
    [Fact]
    public void Project_rebuilt_coverage_layer_inherits_prebuilt_display_state()
    {
        const string datasetId = "s104.h5";
        var original = BuildCoverageGridItem(datasetId);
        var originalGrid = (GridCoverageSubLayer)((CoverageStackPayload)original.Payload).SubLayer;

        // The engine replaces the sub-layer (new reference) — mirrors R-101-104-B
        // attaching a land mask to the S-104 surface.
        var ruledGrid = originalGrid.WithLandAreaMask(null);
        var ruled = new SubLayerStackItem(
            ((CoverageStackPayload)original.Payload).WithSubLayer(ruledGrid),
            S98DisplayPlane.OnDemandSurface,
            0,
            datasetId);

        // Prebuilt layer carries the user's current display state.
        var prebuiltLayer = new MemoryLayer
        {
            Name = originalGrid.LayerName,
            Enabled = false,
            Opacity = 0.5,
            MinVisible = 100.0,
            MaxVisible = 200.0,
        };
        var prebuilt = new Dictionary<(string, string), LayerStackEntry>
        {
            [LayerStackProjector.KeyOf(original)] = new LayerStackEntry(prebuiltLayer, original),
        };

        // The rebuild produces a fresh layer that defaults to Enabled=true /
        // Opacity=1 with no visible range.
        var rebuiltLayer = new MemoryLayer { Name = originalGrid.LayerName };

        var projected = LayerStackProjector.Project(
            new[] { ruled },
            prebuilt,
            _ => rebuiltLayer);

        var layer = Assert.Single(projected).Layer;
        Assert.Same(rebuiltLayer, layer);
        Assert.False(layer.Enabled);
        Assert.Equal(0.5, layer.Opacity);
        Assert.Equal(100.0, layer.MinVisible);
        Assert.Equal(200.0, layer.MaxVisible);
    }

    [Fact]
    public void Project_suppressed_vector_layer_keeps_its_tiled_scene_minus_the_dropped_features()
    {
        // R-101-102-B dropped DepthArea "1"; the pattern fill "2" survives. The
        // filtered layer must still be painted by the tiled renderer from the
        // bound scene: its Mapsui features are pick targets only, so falling
        // back to them would lose pattern fills and tiling.
        const string datasetId = "s101.000";
        var original = BuildVectorItem(datasetId, "1", "2");
        var ruled = new SubLayerStackItem(
            ((VectorStackPayload)original.Payload).WithSubLayer(BuildAreaSubLayer("2")),
            S98DisplayPlane.BaseChartUnder,
            0,
            datasetId);

        var square = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 0.0) };
        var prebuiltLayer = new InstrumentedMemoryLayer
        {
            Name = "S-101 (areas)",
            Features = new[] { PickFeature("1"), PickFeature("2") },
            CustomLayerRendererName = S100VectorTileRenderer.RendererName,
            RepeatsAcrossWorldCopies = true,
            MaxVisible = 300.0,
        };
        S100VectorTileRenderer.BindScene(prebuiltLayer, new VectorScene(new PaintOp[]
        {
            new AreaPaintOp { FeatureReference = "1", WorldShell = square },
            new PatternAreaPaintOp
            {
                FeatureReference = "2", PatternReference = "DIAMOND1", WorldShell = square, TilePng = [],
            },
            new PointPaintOp { FeatureReference = "2", World = (0.5, 0.5) },
        }));
        var prebuilt = new Dictionary<(string, string), LayerStackEntry>
        {
            [LayerStackProjector.KeyOf(original)] = new LayerStackEntry(prebuiltLayer, original),
        };

        // The filtered layer keeps the tiled source's world-copy pick features
        // (issue #773).
        var layer = Assert.IsType<WorldCopyMemoryLayer>(Assert.Single(LayerStackProjector.Project(new[] { ruled }, prebuilt)).Layer);

        Assert.NotSame(prebuiltLayer, layer);
        Assert.Equal(S100VectorTileRenderer.RendererName, layer.CustomLayerRendererName);
        Assert.Equal(300.0, layer.MaxVisible);
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(layer, out var baseScene, out var overlayScene));
        Assert.IsType<PatternAreaPaintOp>(Assert.Single(baseScene.Ops));
        Assert.Equal("2", Assert.Single(overlayScene.Ops).FeatureReference);
        Assert.Equal("2", Assert.Single(layer.Features)[MapsuiDisplayListRenderer.FeatureRefKey]);
    }

    private static GeometryFeature PickFeature(string featureReference)
    {
        var feature = new GeometryFeature(new Point(0, 0));
        feature[MapsuiDisplayListRenderer.FeatureRefKey] = featureReference;
        return feature;
    }

    private static VectorSubLayer BuildAreaSubLayer(params string[] featureReferences) => new()
    {
        LayerKey = "s101.areas",
        LayerName = "S-101 (areas)",
        Instructions = featureReferences
            .Select(r => (DrawingInstruction)new AreaInstruction { FeatureReference = r, FillColor = "DEPVS" })
            .ToList(),
        Plane = S98DisplayPlane.BaseChartUnder,
        SourceFeatureType = "area",
    };

    private static SubLayerStackItem BuildVectorItem(string datasetId, params string[] featureReferences)
    {
        var subLayer = BuildAreaSubLayer(featureReferences);
        var result = new VectorPortrayalResult
        {
            SubLayers = new[] { subLayer },
            Palette = new ColorPalette("test", new Dictionary<string, string>()),
            GeometryProvider = new NoGeometry(),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new SubLayerStackItem(
            new VectorStackPayload(result, subLayer), S98DisplayPlane.BaseChartUnder, 0, datasetId);
    }

    private sealed class NoGeometry : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => null;
    }

    private static SubLayerStackItem BuildCoverageGridItem(string datasetId)
    {
        var metadata = new GridMetadata
        {
            NumRows = 2,
            NumColumns = 2,
            OriginLatitude = 0.0,
            OriginLongitude = 0.0,
            SpacingLatitudinal = 1.0,
            SpacingLongitudinal = 1.0,
        };
        var sampled = new SampledCoverage
        {
            Region = GridRegion.Full,
            Metadata = metadata,
            Values = new Dictionary<string, float[]> { ["waterLevelHeight"] = new float[] { 0f, 0f, 0f, 0f } },
        };
        var styled = new StyledCoverageLayer
        {
            Coverage = sampled,
            NoDataValue = float.NaN,
            Georeferencer = new GridGeoreferencer(metadata, "EPSG:4326"),
        };
        var viewport = new Viewport
        {
            MinLatitude = 0.0,
            MaxLatitude = 2.0,
            MinLongitude = 0.0,
            MaxLongitude = 2.0,
            WidthPixels = 1,
            HeightPixels = 1,
            ScaleDenominator = 1.0,
        };
        var grid = new GridCoverageSubLayer
        {
            LayerKey = "s104.surface",
            LayerName = "S-104 surface",
            Plane = S98DisplayPlane.OnDemandSurface,
            Coverage = styled,
            Viewport = viewport,
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = new[] { grid },
            Spec = new SpecRef("S-104", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        return new SubLayerStackItem(new CoverageStackPayload(result, grid), S98DisplayPlane.OnDemandSurface, 0, datasetId);
    }
}
