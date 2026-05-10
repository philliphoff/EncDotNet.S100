using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S124.Tests;

/// <summary>
/// Tests for <see cref="S124DatasetReader"/> using synthetic S-124 GML datasets.
/// </summary>
public class S124DatasetReaderTests
{
    private const string TestDataDir = "TestData";

    private static S124Dataset LoadTestData(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S124Dataset.Open(path);
    }

    // ── Point dataset ────────────────────────────────────────────

    [Fact]
    public void PointDataset_ParsesProductIdentifier()
    {
        var ds = LoadTestData("navwarn_point.gml");
        Assert.Equal("S-124", ds.ProductIdentifier);
    }

    [Fact]
    public void PointDataset_ParsesDatasetId()
    {
        var ds = LoadTestData("navwarn_point.gml");
        Assert.Equal("DS_NavWarn_Point_Test", ds.DatasetIdentifier);
    }

    [Fact]
    public void PointDataset_HasThreeFeatures()
    {
        var ds = LoadTestData("navwarn_point.gml");
        Assert.Equal(3, ds.Features.Length);
    }

    [Fact]
    public void PointDataset_HasOneInformationType()
    {
        var ds = LoadTestData("navwarn_point.gml");
        Assert.Single(ds.InformationTypes);
        Assert.Equal("NavwarnPreamble", ds.InformationTypes[0].TypeCode);
    }

    [Fact]
    public void PointDataset_NavwarnPart_HasPointGeometry()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.Equal("NavwarnPart", f1.FeatureType);
        Assert.Equal(GmlGeometryType.Point, f1.GeometryType);
        Assert.Single(f1.Points);
        Assert.Equal(36.95, f1.Points[0].Latitude, 4);
        Assert.Equal(-76.0133, f1.Points[0].Longitude, 4);
    }

    [Fact]
    public void PointDataset_NavwarnPart_HasAttributes()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.True(f1.Attributes.ContainsKey("restriction"));
        Assert.Equal("7", f1.Attributes["restriction"]);
    }

    [Fact]
    public void PointDataset_NavwarnPart_HasComplexAttribute()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        var warningInfo = f1.ComplexAttributes.FirstOrDefault(c => c.Code == "warningInformation");
        Assert.NotNull(warningInfo);
        Assert.True(warningInfo.SubAttributes.ContainsKey("information"));
        Assert.Equal("Buoy reported off station", warningInfo.SubAttributes["information"]);
    }

    [Fact]
    public void PointDataset_TextPlacement_HasPointGeometry()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var f3 = ds.Features.First(f => f.Id == "f3");

        Assert.Equal("TextPlacement", f3.FeatureType);
        Assert.Equal(GmlGeometryType.Point, f3.GeometryType);
        Assert.Single(f3.Points);
    }

    [Fact]
    public void PointDataset_InformationType_HasComplexAttributes()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var preamble = ds.InformationTypes[0];

        Assert.Equal("NavwarnPreamble", preamble.TypeCode);
        var msi = preamble.ComplexAttributes.FirstOrDefault(c => c.Code == "messageSeriesIdentifier");
        Assert.NotNull(msi);
        Assert.Equal("42", msi.SubAttributes["warningNumber"]);
        Assert.Equal("2026", msi.SubAttributes["year"]);
    }

    // ── Curve dataset ────────────────────────────────────────────

    [Fact]
    public void CurveDataset_HasTwoFeatures()
    {
        var ds = LoadTestData("navwarn_curve.gml");
        Assert.Equal(2, ds.Features.Length);
    }

    [Fact]
    public void CurveDataset_NavwarnPart_HasCurveGeometry()
    {
        var ds = LoadTestData("navwarn_curve.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.Equal(GmlGeometryType.Curve, f1.GeometryType);
        Assert.Single(f1.Curves);
        Assert.Equal(4, f1.Curves[0].Length);
        Assert.Equal(29.31, f1.Curves[0][0].Latitude, 4);
        Assert.Equal(-94.78, f1.Curves[0][0].Longitude, 4);
    }

    [Fact]
    public void CurveDataset_SecondCurve_HasThreePoints()
    {
        var ds = LoadTestData("navwarn_curve.gml");
        var f2 = ds.Features.First(f => f.Id == "f2");

        Assert.Equal(GmlGeometryType.Curve, f2.GeometryType);
        Assert.Single(f2.Curves);
        Assert.Equal(3, f2.Curves[0].Length);
    }

    // ── Surface dataset ──────────────────────────────────────────

    [Fact]
    public void SurfaceDataset_HasThreeFeatures()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        Assert.Equal(3, ds.Features.Length);
    }

    [Fact]
    public void SurfaceDataset_HasTwoInformationTypes()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        Assert.Equal(2, ds.InformationTypes.Length);
    }

    [Fact]
    public void SurfaceDataset_NavwarnAreaAffected_HasSurfaceGeometry()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        var f1 = ds.Features.First(f => f.Id == "f1");

        Assert.Equal("NavwarnAreaAffected", f1.FeatureType);
        Assert.Equal(GmlGeometryType.Surface, f1.GeometryType);
        Assert.Equal(5, f1.ExteriorRing.Length); // Closed ring: 5 coordinate pairs
        Assert.Equal(51.05, f1.ExteriorRing[0].Latitude, 4);
        Assert.Equal(1.20, f1.ExteriorRing[0].Longitude, 4);
    }

    [Fact]
    public void SurfaceDataset_SurfaceWithHole_HasInteriorRing()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        var f3 = ds.Features.First(f => f.Id == "f3");

        Assert.Equal(GmlGeometryType.Surface, f3.GeometryType);
        Assert.Equal(5, f3.ExteriorRing.Length);
        Assert.Single(f3.InteriorRings);
        Assert.Equal(5, f3.InteriorRings[0].Length);
    }

    [Fact]
    public void SurfaceDataset_PointInsideArea_ParsedCorrectly()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        var f2 = ds.Features.First(f => f.Id == "f2");

        Assert.Equal("NavwarnPart", f2.FeatureType);
        Assert.Equal(GmlGeometryType.Point, f2.GeometryType);
        Assert.Single(f2.Points);
        Assert.Equal(51.085, f2.Points[0].Latitude, 4);
    }

    // ── Mixed dataset ────────────────────────────────────────────

    [Fact]
    public void MixedDataset_HasFiveFeatures()
    {
        var ds = LoadTestData("navwarn_mixed.gml");
        Assert.Equal(5, ds.Features.Length);
    }

    [Fact]
    public void MixedDataset_HasThreeInformationTypes()
    {
        var ds = LoadTestData("navwarn_mixed.gml");
        Assert.Equal(3, ds.InformationTypes.Length);
    }

    [Fact]
    public void MixedDataset_ContainsAllGeometryTypes()
    {
        var ds = LoadTestData("navwarn_mixed.gml");

        Assert.Contains(ds.Features, f => f.GeometryType == GmlGeometryType.Point);
        Assert.Contains(ds.Features, f => f.GeometryType == GmlGeometryType.Curve);
        Assert.Contains(ds.Features, f => f.GeometryType == GmlGeometryType.Surface);
        Assert.Contains(ds.Features, f => f.GeometryType == GmlGeometryType.None);
    }

    [Fact]
    public void MixedDataset_ContainsAllFeatureTypes()
    {
        var ds = LoadTestData("navwarn_mixed.gml");

        var types = ds.Features.Select(f => f.FeatureType).Distinct().ToHashSet();
        Assert.Contains("NavwarnPart", types);
        Assert.Contains("NavwarnAreaAffected", types);
        Assert.Contains("TextPlacement", types);
    }

    [Fact]
    public void MixedDataset_ContainsAllInformationTypes()
    {
        var ds = LoadTestData("navwarn_mixed.gml");

        var types = ds.InformationTypes.Select(i => i.TypeCode).Distinct().ToHashSet();
        Assert.Contains("NavwarnPreamble", types);
        Assert.Contains("References", types);
        Assert.Contains("SpatialQuality", types);
    }

    [Fact]
    public void MixedDataset_NoGeometryFeature_HasNoPoints()
    {
        var ds = LoadTestData("navwarn_mixed.gml");
        var f5 = ds.Features.First(f => f.Id == "f5");

        Assert.Equal(GmlGeometryType.None, f5.GeometryType);
        Assert.True(f5.Points.IsDefaultOrEmpty);
        Assert.True(f5.Curves.IsDefaultOrEmpty);
        Assert.True(f5.ExteriorRing.IsDefaultOrEmpty);
    }
}
