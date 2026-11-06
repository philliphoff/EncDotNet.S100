using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using Mapsui.Layers;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins <see cref="LayerStackProjector.Project(IReadOnlyList{SubLayerStackItem}, IReadOnlyDictionary{ValueTuple{string, string}, LayerStackEntry}, Func{CoverageSubLayerBase, Mapsui.Layers.ILayer})"/>'s coverage-rebuild branch:
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
    public void Project_item_aware_rebuild_receives_the_ruled_item()
    {
        // The session keys its per-dataset coverage layout cache by the ruled
        // item's SourceDatasetId (issue #855), so the rebuild must see the item.
        const string datasetId = "s104.h5";
        var original = BuildCoverageGridItem(datasetId);
        var originalGrid = (GridCoverageSubLayer)((CoverageStackPayload)original.Payload).SubLayer;
        var ruledGrid = originalGrid.WithLandAreaMask(null);
        var ruled = new SubLayerStackItem(
            ((CoverageStackPayload)original.Payload).WithSubLayer(ruledGrid),
            S98DisplayPlane.OnDemandSurface,
            0,
            datasetId);
        var prebuilt = new Dictionary<(string, string), LayerStackEntry>
        {
            [LayerStackProjector.KeyOf(original)] = new LayerStackEntry(new MemoryLayer(), original),
        };
        var rebuiltLayer = new MemoryLayer();
        SubLayerStackItem? seenItem = null;
        CoverageSubLayerBase? seenSubLayer = null;

        var projected = LayerStackProjector.Project(
            new[] { ruled },
            prebuilt,
            (item, subLayer) =>
            {
                seenItem = item;
                seenSubLayer = subLayer;
                return rebuiltLayer;
            });

        Assert.Same(rebuiltLayer, Assert.Single(projected).Layer);
        Assert.Same(ruled, seenItem);
        Assert.Same(ruledGrid, seenSubLayer);
    }

    [Fact]
    public void Project_rebuilds_arrows_a_rule_replaced()
    {
        // R-111-WX-B (issue #1228) hands back model current arrows with their
        // speed blanked inside S-111; the projector must draw those, not the
        // prebuilt arrows.
        const string datasetId = "currents.nc";
        var grid = (GridCoverageSubLayer)((CoverageStackPayload)BuildCoverageGridItem(datasetId).Payload).SubLayer;
        var arrows = new ArrowCoverageSubLayer
        {
            LayerKey = "weather.current.arrows",
            LayerName = "Currents",
            Plane = S98DisplayPlane.DynamicArrows,
            Coverage = grid.Coverage,
            Viewport = grid.Viewport,
            SymbolProvider = _ => null,
            FallbackExtent = new GeographicBounds(0, 0, 2, 2),
            YieldsToS111 = true,
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = new[] { arrows },
            Spec = new SpecRef("NetCDF", default),
            SourceDatasetId = datasetId,
            Info = "test",
        };
        var original = new SubLayerStackItem(new CoverageStackPayload(result, arrows), S98DisplayPlane.DynamicArrows, 20, datasetId);
        var masked = arrows.WithCoverage(grid.Coverage);
        var ruled = original with { Payload = ((CoverageStackPayload)original.Payload).WithSubLayer(masked) };
        var prebuilt = new Dictionary<(string, string), LayerStackEntry>
        {
            [LayerStackProjector.KeyOf(original)] = new LayerStackEntry(new MemoryLayer(), original),
        };
        var rebuiltLayer = new MemoryLayer();
        CoverageSubLayerBase? seen = null;

        var projected = LayerStackProjector.Project(new[] { ruled }, prebuilt, subLayer =>
        {
            seen = subLayer;
            return rebuiltLayer;
        });

        Assert.Same(rebuiltLayer, Assert.Single(projected).Layer);
        Assert.Same(masked, seen);
    }

    [Fact]
    public void Project_suppressed_vector_layer_keeps_its_layer_and_records_the_dropped_features()
    {
        // R-101-102-B dropped DepthArea "1"; "2" survives. The prebuilt layer
        // stays on the stack, and the dropped features become a filter on its
        // scene's ops (map rendering slice 8), not a filtered copy of the layer.
        const string datasetId = "s101.000";
        var original = BuildVectorItem(datasetId, "1", "2");
        var prebuilt = TiledPrebuilt(original);

        var entry = Assert.Single(LayerStackProjector.Project(new[] { Ruled(original, "2") }, prebuilt));

        Assert.Same(prebuilt[LayerStackProjector.KeyOf(original)].Layer, entry.Layer);
        Assert.Equal(new HashSet<string> { "1" }, entry.SuppressedFeatures);
    }

    [Fact]
    public void Project_unsuppressed_vector_layer_has_no_suppressed_features()
    {
        const string datasetId = "s101.000";
        var original = BuildVectorItem(datasetId, "1", "2");
        var prebuilt = TiledPrebuilt(original);

        var entry = Assert.Single(LayerStackProjector.Project(new[] { Ruled(original, "1", "2") }, prebuilt));

        Assert.Same(prebuilt[LayerStackProjector.KeyOf(original)].Layer, entry.Layer);
        Assert.Null(entry.SuppressedFeatures);
    }

    private static Dictionary<(string, string), LayerStackEntry> TiledPrebuilt(SubLayerStackItem original)
    {
        var refs = ((VectorStackPayload)original.Payload).SubLayer.Instructions
            .Select(i => i.FeatureReference!)
            .ToArray();
        var square = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 0.0) };
        var layer = new InstrumentedMemoryLayer
        {
            Name = "S-101 (areas)",
            Features = refs.Select(PickFeature).ToList(),
            CustomLayerRendererName = S100VectorTileRenderer.RendererName,
            RepeatsAcrossWorldCopies = true,
        };
        S100VectorTileRenderer.BindScene(layer, new VectorScene(
            refs.Select(r => (PaintOp)new AreaPaintOp { FeatureReference = r, WorldShell = square }).ToArray()));
        return new Dictionary<(string, string), LayerStackEntry>
        {
            [LayerStackProjector.KeyOf(original)] = new LayerStackEntry(layer, original),
        };
    }

    /// <summary><paramref name="original"/> re-ruled with only <paramref name="surviving"/> kept.</summary>
    private static SubLayerStackItem Ruled(SubLayerStackItem original, params string[] surviving) =>
        new(
            ((VectorStackPayload)original.Payload).WithSubLayer(BuildAreaSubLayer(surviving)),
            S98DisplayPlane.BaseChartUnder,
            0,
            original.SourceDatasetId);

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
