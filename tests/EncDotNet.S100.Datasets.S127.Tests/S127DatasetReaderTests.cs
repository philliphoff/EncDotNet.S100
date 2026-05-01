using EncDotNet.S100.Datasets.S127;

namespace EncDotNet.S100.Datasets.S127.Tests;

/// <summary>
/// Tests for <see cref="S127DatasetReader"/> using synthetic S-127 GML datasets.
/// </summary>
public class S127DatasetReaderTests
{
    private const string TestDataDir = "TestData";

    private static S127Dataset LoadTestData(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S127Dataset.Open(path);
    }

    // ── Point dataset ────────────────────────────────────────────

    [Fact]
    public void PointDataset_ParsesProductIdentifier()
    {
        var ds = LoadTestData("marine_point.gml");
        Assert.Equal("S-127", ds.ProductIdentifier);
    }

    [Fact]
    public void PointDataset_ParsesDatasetId()
    {
        var ds = LoadTestData("marine_point.gml");
        Assert.Equal("DS_S127_Point_Test", ds.DatasetIdentifier);
    }

    [Fact]
    public void PointDataset_HasThreeFeatures()
    {
        var ds = LoadTestData("marine_point.gml");
        Assert.Equal(3, ds.Features.Length);
    }

    [Fact]
    public void PointDataset_PilotBoardingPlace_HasPointGeometry()
    {
        var ds = LoadTestData("marine_point.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.Equal("PilotBoardingPlace", f1.FeatureType);
        Assert.Equal(S127GeometryType.Point, f1.GeometryType);
        Assert.Single(f1.Points);
        Assert.Equal(36.95, f1.Points[0].Latitude, 4);
        Assert.Equal(-76.0133, f1.Points[0].Longitude, 4);
    }

    [Fact]
    public void PointDataset_PilotBoardingPlace_HasSimpleAttribute()
    {
        var ds = LoadTestData("marine_point.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.True(f1.Attributes.ContainsKey("categoryOfPilotBoardingPlace"));
        Assert.Equal("1", f1.Attributes["categoryOfPilotBoardingPlace"]);
    }

    [Fact]
    public void PointDataset_PilotBoardingPlace_HasComplexAttribute()
    {
        var ds = LoadTestData("marine_point.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        var contact = f1.ComplexAttributes.FirstOrDefault(c => c.Code == "contactDetails");
        Assert.NotNull(contact);
        Assert.True(contact.SubAttributes.ContainsKey("callName"));
        Assert.Equal("HAMPTON ROADS PILOT", contact.SubAttributes["callName"]);
    }

    [Fact]
    public void PointDataset_HasNoInformationTypes()
    {
        var ds = LoadTestData("marine_point.gml");
        // S-127 Edition 2.0.0 has no information types.
        Assert.Empty(ds.InformationTypes);
    }

    // ── Curve dataset ────────────────────────────────────────────

    [Fact]
    public void CurveDataset_HasTwoFeatures()
    {
        var ds = LoadTestData("marine_curve.gml");
        Assert.Equal(2, ds.Features.Length);
    }

    [Fact]
    public void CurveDataset_RouteingMeasure_HasCurveGeometry()
    {
        var ds = LoadTestData("marine_curve.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.Equal("RouteingMeasure", f1.FeatureType);
        Assert.Equal(S127GeometryType.Curve, f1.GeometryType);
        Assert.Single(f1.Curves);
        Assert.Equal(4, f1.Curves[0].Length);
        Assert.Equal(29.31, f1.Curves[0][0].Latitude, 4);
        Assert.Equal(-94.78, f1.Curves[0][0].Longitude, 4);
    }

    [Fact]
    public void CurveDataset_SecondCurve_HasThreePoints()
    {
        var ds = LoadTestData("marine_curve.gml");
        var f2 = ds.Features.First(f => f.Id == "f2");

        Assert.Equal(S127GeometryType.Curve, f2.GeometryType);
        Assert.Single(f2.Curves);
        Assert.Equal(3, f2.Curves[0].Length);
    }

    // ── Surface dataset ──────────────────────────────────────────

    [Fact]
    public void SurfaceDataset_HasThreeFeatures()
    {
        var ds = LoadTestData("marine_surface.gml");
        Assert.Equal(3, ds.Features.Length);
    }

    [Fact]
    public void SurfaceDataset_RestrictedArea_HasSurfaceGeometry()
    {
        var ds = LoadTestData("marine_surface.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.Equal("RestrictedArea", f1.FeatureType);
        Assert.Equal(S127GeometryType.Surface, f1.GeometryType);
        Assert.Equal(5, f1.ExteriorRing.Length); // closed ring
        Assert.Equal(51.05, f1.ExteriorRing[0].Latitude, 4);
        Assert.Equal(1.20, f1.ExteriorRing[0].Longitude, 4);
    }

    [Fact]
    public void SurfaceDataset_VtsArea_HasInteriorRing()
    {
        var ds = LoadTestData("marine_surface.gml");
        var f3 = ds.Features.First(f => f.Id == "f3");

        Assert.Equal("VesselTrafficServiceArea", f3.FeatureType);
        Assert.Equal(S127GeometryType.Surface, f3.GeometryType);
        Assert.Equal(5, f3.ExteriorRing.Length);
        Assert.Single(f3.InteriorRings);
        Assert.Equal(5, f3.InteriorRings[0].Length);
    }

    [Fact]
    public void SurfaceDataset_PointInsideArea_ParsedCorrectly()
    {
        var ds = LoadTestData("marine_surface.gml");
        var f2 = ds.Features.First(f => f.Id == "f2");

        Assert.Equal("PilotBoardingPlace", f2.FeatureType);
        Assert.Equal(S127GeometryType.Point, f2.GeometryType);
        Assert.Single(f2.Points);
        Assert.Equal(51.085, f2.Points[0].Latitude, 4);
    }

    // ── Mixed dataset ────────────────────────────────────────────

    [Fact]
    public void MixedDataset_HasFiveFeatures()
    {
        var ds = LoadTestData("marine_mixed.gml");
        Assert.Equal(5, ds.Features.Length);
    }

    [Fact]
    public void MixedDataset_ContainsAllGeometryTypes()
    {
        var ds = LoadTestData("marine_mixed.gml");

        Assert.Contains(ds.Features, f => f.GeometryType == S127GeometryType.Point);
        Assert.Contains(ds.Features, f => f.GeometryType == S127GeometryType.Curve);
        Assert.Contains(ds.Features, f => f.GeometryType == S127GeometryType.Surface);
        Assert.Contains(ds.Features, f => f.GeometryType == S127GeometryType.None);
    }

    [Fact]
    public void MixedDataset_ContainsExpectedFeatureTypes()
    {
        var ds = LoadTestData("marine_mixed.gml");

        var types = ds.Features.Select(f => f.FeatureType).Distinct().ToHashSet();
        Assert.Contains("PilotBoardingPlace", types);
        Assert.Contains("RouteingMeasure", types);
        Assert.Contains("RestrictedArea", types);
        Assert.Contains("SignalStationTraffic", types);
        Assert.Contains("Authority", types);
    }

    [Fact]
    public void MixedDataset_NoGeometryFeature_HasNoPoints()
    {
        var ds = LoadTestData("marine_mixed.gml");
        var f5 = ds.Features.First(f => f.Id == "f5");

        Assert.Equal(S127GeometryType.None, f5.GeometryType);
        Assert.True(f5.Points.IsDefaultOrEmpty);
        Assert.True(f5.Curves.IsDefaultOrEmpty);
        Assert.True(f5.ExteriorRing.IsDefaultOrEmpty);
    }

    // ── Legacy S-100 GML 1.0 namespace shape (Edition 1.0.1, 2019) ──

    [Fact]
    public void LegacyDataset_ParsesAllThreeGeometryKinds()
    {
        // The 2019 S-127 Edition 1.0.1 reference dataset declares the S-100
        // GML namespace as `http://www.iho.int/s100gml/1.0` (no `/profile/`)
        // and uses an unprefixed `<geometry>` wrapper. The reader must
        // tolerate both 5.0 and the two 1.0 spellings.
        var ds = LoadTestData("marine_legacy_s100gml1.gml");

        Assert.Equal(3, ds.Features.Length);
        Assert.Equal(
            S127GeometryType.Point,
            ds.Features.First(f => f.Id == "f1").GeometryType);
        Assert.Equal(
            S127GeometryType.Surface,
            ds.Features.First(f => f.Id == "f2").GeometryType);
        Assert.Equal(
            S127GeometryType.Curve,
            ds.Features.First(f => f.Id == "f3").GeometryType);
    }
}
