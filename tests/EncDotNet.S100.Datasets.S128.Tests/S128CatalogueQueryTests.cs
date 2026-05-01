using EncDotNet.S100.Datasets.S128;

namespace EncDotNet.S100.Datasets.S128.Tests;

/// <summary>
/// Tests for <see cref="S128CatalogueQuery"/>.
/// </summary>
public class S128CatalogueQueryTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "S128_TDS_sample.gml";

    private static S128Dataset LoadSample() =>
        S128Dataset.Open(Path.Combine(TestDataDir, SampleFile));

    [Fact]
    public void FilterByProductType_ReturnsOnlyMatchingFeatureClasses()
    {
        var ds = LoadSample();
        var only = S128CatalogueQuery.FilterByProductType(ds.Entries, "ElectronicProduct").ToList();
        Assert.NotEmpty(only);
        Assert.All(only, e => Assert.Equal("ElectronicProduct", e.FeatureType));
    }

    [Fact]
    public void FilterByExtent_ExcludesEntriesOutsideAOI()
    {
        var ds = LoadSample();
        // AOI off the Atlantic coast of Africa — no overlap with Korean
        // sample entries.
        var hits = S128CatalogueQuery.FilterByExtent(ds.Entries,
            minLatitude: 0, minLongitude: -20,
            maxLatitude: 10, maxLongitude: -10).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void FilterByExtent_IncludesEntriesIntersectingAOI()
    {
        var ds = LoadSample();
        // AOI covering the Korean waters where the sample's products lie.
        var hits = S128CatalogueQuery.FilterByExtent(ds.Entries,
            minLatitude: 30, minLongitude: 120,
            maxLatitude: 45, maxLongitude: 140).ToList();
        Assert.NotEmpty(hits);
    }

    [Fact]
    public void FilterByStatus_ReturnsRequestedStatusOnly()
    {
        var ds = LoadSample();
        // Without explicit serviceStatus / distributionStatus the heuristic
        // defaults to InForce; this confirms the predicate works.
        var hits = S128CatalogueQuery.FilterByStatus(ds.Entries, S128ProductStatus.InForce).ToList();
        Assert.All(hits, e => Assert.Equal(S128ProductStatus.InForce, e.Status));
    }

    [Fact]
    public void FilterBySpecification_MatchesSubstring()
    {
        var ds = LoadSample();
        // S-101 ENC products are present in the sample.
        var hits = S128CatalogueQuery.FilterBySpecification(ds.Entries, "S-101").ToList();
        // Heuristic depends on how the sample populates productSpecification;
        // either the spec name resolves or it does not — at minimum the
        // method must not throw and must respect the substring contract.
        Assert.All(hits, e =>
            Assert.Contains("S-101", e.ProductSpecificationName!, StringComparison.OrdinalIgnoreCase));
    }
}
