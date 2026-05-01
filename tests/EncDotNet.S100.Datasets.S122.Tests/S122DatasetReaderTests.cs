using EncDotNet.S100.Datasets.S122;

namespace EncDotNet.S100.Datasets.S122.Tests;

/// <summary>
/// Tests for <see cref="S122DatasetReader"/> using the official S-122 2.0.0
/// sample dataset (<c>122TESTDATASET.gml</c>).
/// </summary>
public class S122DatasetReaderTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "122TESTDATASET.gml";

    private static S122Dataset LoadSample()
    {
        var path = Path.Combine(TestDataDir, SampleFile);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S122Dataset.Open(path);
    }

    [Fact]
    public void Sample_ParsesProductIdentifier()
    {
        // The sample's <S100:productIdentifier> is "INT.IHO.S-122.1.0.0".
        var ds = LoadSample();
        Assert.NotNull(ds.ProductIdentifier);
        Assert.Contains("S-122", ds.ProductIdentifier!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sample_ParsesDatasetId()
    {
        var ds = LoadSample();
        Assert.Equal("KRNPI123_TEST002_001", ds.DatasetIdentifier);
    }

    [Fact]
    public void Sample_HasFourFeatures()
    {
        var ds = LoadSample();
        Assert.Equal(4, ds.Features.Length);
    }

    [Fact]
    public void Sample_ContainsExpectedFeatureTypes()
    {
        var ds = LoadSample();
        var types = ds.Features.Select(f => f.FeatureType).Distinct().ToHashSet();

        Assert.Contains("MarineProtectedArea", types);
        Assert.Contains("RestrictedArea", types);
        Assert.Contains("VesselTrafficServiceArea", types);
    }

    [Fact]
    public void Sample_MarineProtectedArea_Surface_HasExteriorRing()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0003");

        Assert.Equal("MarineProtectedArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
        // S-100 Part 10b convention: lat lon for EPSG:4326.
        Assert.Equal(-32.5215215, f.ExteriorRing[0].Latitude, 5);
        Assert.Equal(60.9745257, f.ExteriorRing[0].Longitude, 5);
    }

    [Fact]
    public void Sample_RestrictedArea_HasSurfaceGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0001");

        Assert.Equal("RestrictedArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
    }

    [Fact]
    public void Sample_MarineProtectedArea_Curve_HasCurveGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0004");

        Assert.Equal("MarineProtectedArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Curve, f.GeometryType);
        Assert.Single(f.Curves);
        Assert.Equal(4, f.Curves[0].Length);
    }

    [Fact]
    public void Sample_VesselTrafficServiceArea_HasSurfaceGeometry()
    {
        var ds = LoadSample();
        var f = ds.Features.First(x => x.Id == "FEATURE_ID_0002");

        Assert.Equal("VesselTrafficServiceArea", f.FeatureType);
        Assert.Equal(S122GeometryType.Surface, f.GeometryType);
        Assert.Equal(5, f.ExteriorRing.Length);
    }

    [Fact]
    public void Sample_HasNoInformationTypes()
    {
        var ds = LoadSample();
        Assert.Empty(ds.InformationTypes);
    }
}
