using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;
using EncDotNet.S57;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.S57.Tests.Aml;

/// <summary>
/// AML features are read as they are (AML class acronyms, WGS-84 geometry),
/// and the coverage comes from <c>M_COVR</c> or falls back to the bounding box.
/// </summary>
public class S57ChartFeaturesTests
{
    private static IReadOnlyList<Feature> Read(SyntheticAmlCell.Options options)
    {
        var document = S57DocumentReader.Read(S57DocumentWriter.Write(SyntheticAmlCell.Build("UKC6UT00.000", options)));
        return S57ChartFeatures.Read(S57Chart.FromDocument(document), document.GetObjectCatalogue());
    }

    [Fact]
    public void Read_KeepsAmlClasses_WithGeometryInDegrees()
    {
        var features = Read(new SyntheticAmlCell.Options());

        var point = Assert.Single(features, f => f.Id == SyntheticAmlCell.PointRecordId);
        Assert.Equal(GeometryType.Point, point.GeometryType);
        Assert.Equal(char.ToLowerInvariant(point.FeatureType[0]), point.FeatureType[0]); // AML acronyms are lower case.
        var position = Assert.Single(point.Coordinates);
        Assert.Equal(SyntheticAmlCell.PointLatitude, position.Latitude, 6);
        Assert.Equal(SyntheticAmlCell.PointLongitude, position.Longitude, 6);

        var coverage = Assert.Single(features, f => f.FeatureType == S57ChartFeatures.CoverageClass);
        Assert.Equal(GeometryType.Surface, coverage.GeometryType);
        Assert.Equal(5, coverage.Coordinates.Count);
        Assert.Equal(coverage.Coordinates[0], coverage.Coordinates[^1]);
        Assert.Equal("1", coverage.Attributes[S57ChartFeatures.CategoryOfCoverage]);
    }

    [Fact]
    public void ResolveCoverage_UsesMCovr()
    {
        var coverage = S57ChartFeatures.ResolveCoverage(Read(new SyntheticAmlCell.Options()), out var isBoundingBox);

        Assert.False(isBoundingBox);
        var area = Assert.Single(coverage);
        Assert.True(area.Contains(SyntheticAmlCell.PointLatitude, SyntheticAmlCell.PointLongitude));
        Assert.False(area.Contains(51, -1.05));
    }

    [Fact]
    public void ResolveCoverage_FallsBackToTheBoundingBox_WithoutMCovr()
    {
        var coverage = S57ChartFeatures.ResolveCoverage(Read(new SyntheticAmlCell.Options { WithCoverage = false }), out var isBoundingBox);

        Assert.True(isBoundingBox);
        var ring = Assert.Single(coverage).ExteriorRing;
        Assert.All(ring, p =>
        {
            Assert.Equal(SyntheticAmlCell.PointLatitude, p.Latitude, 6);
            Assert.Equal(SyntheticAmlCell.PointLongitude, p.Longitude, 6);
        });
    }
}
