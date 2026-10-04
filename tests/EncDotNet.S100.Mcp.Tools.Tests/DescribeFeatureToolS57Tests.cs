using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>describe_feature</c> on a legacy S-57 cell: the cell loads translated to
/// the in-memory S-101 model, so its features describe like S-101 ones, with
/// attributes and resolved geometry.
/// </summary>
public class DescribeFeatureToolS57Tests
{
    [Fact]
    public async Task S57_cell_features_describe_with_attributes_and_geometry()
    {
        var catalog = FileDatasetCatalog.Build(
            [new FileDatasetInput(new DatasetId("US5MA1BO"), "S-57", FixturePath())]);
        var loaded = Assert.Single(catalog.Datasets);
        var b = loaded.Bounds;

        var query = await new QueryFeaturesTool(catalog).InvokeAsync(
            new QueryFeaturesRequest(
                new GeoQuery.Box(new GeoBoundingBox(b.SouthLatitude, b.WestLongitude, b.NorthLatitude, b.EastLongitude))));
        Assert.True(query.TryGetValue(out var matches));
        var feature = Assert.IsType<FeatureMatch>(matches.Features.FirstOrDefault());

        var result = await new DescribeFeatureTool(catalog).InvokeAsync(
            new DescribeFeatureRequest(new DatasetId("US5MA1BO"), feature.FeatureId));

        Assert.True(result.TryGetValue(out var value), "describe_feature should support S-57 cells");
        Assert.Equal(feature.FeatureType, value.FeatureTypeName);
        var geometry = value.Attributes.GetProperty("geometry");
        Assert.True(geometry.GetProperty("coordinates").GetArrayLength() > 0);
    }

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", "US5MA1BO.000");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("S-57 fixture US5MA1BO.000 not found above " + AppContext.BaseDirectory);
    }
}
