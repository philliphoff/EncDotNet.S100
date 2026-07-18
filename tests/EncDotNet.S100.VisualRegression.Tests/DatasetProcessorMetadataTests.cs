using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Tests for <see cref="IDatasetProcessor.Metadata"/> — the metadata-as-
/// byproduct surface added in issue #467 (WS1). Verifies that a GML
/// processor derives its <c>DatasetMetadata</c> from the already-parsed
/// features (never a second parse), that the raw <c>Metadata.Extent</c>
/// agrees with the padded render extent, and that the value is memoized.
/// </summary>
public sealed class DatasetProcessorMetadataTests
{
    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }

    private static IDisplayPlaneAuthorityProvider CreateAuthorityProvider() =>
        new DisplayPlaneAuthorityProvider();

    [Fact]
    public async Task S124_Gml_Metadata_MatchesRenderExtent_AndIsMemoized()
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S124", "navwarn_surface.gml");
        using var manager = CreateCatalogueManager();
        var processor = new S124DatasetProcessor(path, manager, CreateAuthorityProvider());

        var metadata = processor.Metadata;

        Assert.Equal("S-124", metadata.Spec.Name);
        Assert.NotNull(metadata.Extent);

        // Metadata is memoized: repeat access returns the same instance,
        // so the feature scan is not repeated (issue #467 WS1).
        Assert.Same(metadata, processor.Metadata);

        var result = await processor.BuildVectorPortrayalAsync();
        Assert.NotNull(result.GeographicExtent);

        var raw = metadata.Extent!;
        var padded = result.GeographicExtent!.Value;

        // The raw (unpadded) metadata envelope sits inside the padded render
        // extent, and both share the same centre — proof they derive from one
        // scan of the same features.
        Assert.True(raw.WestLongitude >= padded.MinLongitude);
        Assert.True(raw.EastLongitude <= padded.MaxLongitude);
        Assert.True(raw.SouthLatitude >= padded.MinLatitude);
        Assert.True(raw.NorthLatitude <= padded.MaxLatitude);

        var rawCentreLon = (raw.WestLongitude + raw.EastLongitude) / 2.0;
        var rawCentreLat = (raw.SouthLatitude + raw.NorthLatitude) / 2.0;
        var paddedCentreLon = (padded.MinLongitude + padded.MaxLongitude) / 2.0;
        var paddedCentreLat = (padded.MinLatitude + padded.MaxLatitude) / 2.0;

        Assert.Equal(rawCentreLon, paddedCentreLon, 9);
        Assert.Equal(rawCentreLat, paddedCentreLat, 9);
    }
}
