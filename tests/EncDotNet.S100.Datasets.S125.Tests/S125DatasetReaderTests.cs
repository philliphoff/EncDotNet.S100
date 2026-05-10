using System.Linq;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S125.Tests;

public class S125DatasetReaderTests
{
    private static string GetTestDataPath(string filename)
    {
        var basePath = AppContext.BaseDirectory;
        return Path.Combine(basePath, "TestData", filename);
    }

    [Fact]
    public void Open_PointDataset_ReturnsPointFeaturesAndInfoTypes()
    {
        var path = GetTestDataPath("aton_point.gml");
        Assert.True(File.Exists(path), $"Test fixture missing: {path}");

        var dataset = S125Dataset.Open(path);

        Assert.Equal("S-125", dataset.ProductIdentifier);
        Assert.Equal(2, dataset.Features.Length);
        Assert.Single(dataset.InformationTypes);

        var lateralBuoy = dataset.Features.Single(f => f.FeatureType == "LateralBuoy");
        Assert.Equal("f1", lateralBuoy.Id);
        Assert.Equal(GmlGeometryType.Point, lateralBuoy.GeometryType);
        Assert.Single(lateralBuoy.Points);
        Assert.Equal(36.95, lateralBuoy.Points[0].Latitude, 4);
        Assert.Equal(-76.0133, lateralBuoy.Points[0].Longitude, 4);
        Assert.Equal("1", lateralBuoy.Attributes["categoryOfLateralMark"]);

        // Information binding round-trips with the role name from the schema.
        Assert.Single(lateralBuoy.InformationReferences);
        Assert.Equal("AtoNStatus", lateralBuoy.InformationReferences[0].Role);
        Assert.Equal("info1", lateralBuoy.InformationReferences[0].InformationRef);

        var info = dataset.InformationTypes.Single();
        Assert.Equal("AtonStatusInformation", info.TypeCode);
        Assert.Equal("info1", info.Id);
        Assert.Equal("1", info.Attributes["changeTypes"]);
    }

    [Fact]
    public void Open_PointDataset_LighthouseHasComplexAttribute()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_point.gml"));
        var light = dataset.Features.Single(f => f.FeatureType == "Landmark");

        var objectName = Assert.Single(light.ComplexAttributes, c => c.Code == "objectName");
        Assert.Equal("Cape Henry Light", objectName.SubAttributes["name"]);
    }

    [Fact]
    public void Open_CurveDataset_ReturnsCurveFeature()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_curve.gml"));

        var nav = Assert.Single(dataset.Features);
        Assert.Equal("NavigationLine", nav.FeatureType);
        Assert.Equal(GmlGeometryType.Curve, nav.GeometryType);
        var curve = Assert.Single(nav.Curves);
        Assert.Equal(3, curve.Length);
        Assert.Equal(36.95, curve[0].Latitude, 4);
        Assert.Equal("045", nav.Attributes["orientation"]);
    }

    [Fact]
    public void Open_SurfaceDataset_ReturnsClosedRing()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_surface.gml"));

        var coverage = Assert.Single(dataset.Features);
        Assert.Equal("DataCoverage", coverage.FeatureType);
        Assert.Equal(GmlGeometryType.Surface, coverage.GeometryType);
        Assert.Equal(5, coverage.ExteriorRing.Length);
        Assert.Empty(coverage.InteriorRings);
    }
}
