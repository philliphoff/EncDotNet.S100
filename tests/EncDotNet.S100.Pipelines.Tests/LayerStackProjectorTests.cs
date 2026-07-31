using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Coverage;
using Mapsui.Layers;

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
