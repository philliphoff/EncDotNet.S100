using EncDotNet.S100.Datasets.S124;

namespace EncDotNet.S100.Datasets.S124.Tests;

/// <summary>
/// Tests for the phased-loading <c>ReadMetadata</c> "peek" path on
/// <see cref="S124Dataset"/> (issue #460): it must report the declared
/// specification and a geographic extent computed from feature geometry
/// without requiring a full portrayal pass.
/// </summary>
public class S124DatasetMetadataTests
{
    private const string TestDataDir = "TestData";

    private static string Path(string fileName) =>
        System.IO.Path.Combine(TestDataDir, fileName);

    [Fact]
    public void ReadMetadata_ReportsSpecName()
    {
        var metadata = S124Dataset.ReadMetadata(Path("navwarn_point.gml"));

        Assert.Equal("S-124", metadata.Spec.Name);
    }

    [Fact]
    public void ReadMetadata_ComputesExtentFromPointGeometry()
    {
        var metadata = S124Dataset.ReadMetadata(Path("navwarn_point.gml"));

        Assert.NotNull(metadata.Extent);
        // Fixture points: (36.9500,-76.0133), (37.0167,-76.3300), (36.9520,-76.0100).
        Assert.Equal(36.9500, metadata.Extent!.SouthLatitude, 3);
        Assert.Equal(37.0167, metadata.Extent.NorthLatitude, 3);
        Assert.Equal(-76.3300, metadata.Extent.WestLongitude, 3);
        Assert.Equal(-76.0100, metadata.Extent.EastLongitude, 3);
    }

    [Fact]
    public void ReadMetadata_MatchesFullyLoadedDatasetExtent()
    {
        var fromMetadata = S124Dataset.ReadMetadata(Path("navwarn_point.gml"));
        var fromDataset = S124Dataset.Open(Path("navwarn_point.gml")).ReadMetadata();

        Assert.Equal(fromDataset.Extent, fromMetadata.Extent);
    }
}
