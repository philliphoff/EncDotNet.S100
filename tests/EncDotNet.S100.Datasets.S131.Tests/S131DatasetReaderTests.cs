using System.Linq;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S131.Tests;

public class S131DatasetReaderTests
{
    private static string GetTestDataPath(string filename)
    {
        var basePath = AppContext.BaseDirectory;
        return Path.Combine(basePath, "TestData", filename);
    }

    [Fact]
    public void Open_PointDataset_ReturnsPointFeaturesAndInfoTypes()
    {
        var path = GetTestDataPath("harbour_point.gml");
        Assert.True(File.Exists(path), $"Test fixture missing: {path}");

        var dataset = S131Dataset.Open(path);

        // Two point features (Bollard, MooringBuoy) and one info type (ContactDetails)
        Assert.Equal(2, dataset.Features.Length);
        Assert.Single(dataset.InformationTypes);

        // Verify Bollard feature
        var bollard = dataset.Features.Single(f => f.FeatureType == "Bollard");
        Assert.Equal("f1", bollard.Id);
        Assert.Equal(GmlGeometryType.Point, bollard.GeometryType);
        Assert.Single(bollard.Points);
        Assert.Equal(44.6475, bollard.Points[0].Latitude, 4);
        Assert.Equal(-63.5713, bollard.Points[0].Longitude, 4);
        Assert.Equal("B-001", bollard.Attributes["bollardNumber"]);
        Assert.Equal("50", bollard.Attributes["bollardPull"]);

        // Verify MooringBuoy feature
        var buoy = dataset.Features.Single(f => f.FeatureType == "MooringBuoy");
        Assert.Equal("f2", buoy.Id);
        Assert.Equal(GmlGeometryType.Point, buoy.GeometryType);

        // Verify ContactDetails is recognized as information type
        var info = Assert.Single(dataset.InformationTypes);
        Assert.Equal("ContactDetails", info.TypeCode);
        Assert.Equal("info1", info.Id);
        Assert.Equal("Call VHF Ch 12", info.Attributes["contactInstructions"]);
    }

    [Fact]
    public void Open_PointDataset_ComplexAttributeParsed()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_point.gml"));
        var buoy = dataset.Features.Single(f => f.FeatureType == "MooringBuoy");

        var featureName = Assert.Single(buoy.ComplexAttributes, c => c.Code == "featureName");
        Assert.Equal("Mooring Buoy Alpha", featureName.SubAttributes["name"]);
        Assert.Equal("eng", featureName.SubAttributes["language"]);
    }

    [Fact]
    public void Open_CurveDataset_ReturnsCurveFeature()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_curve.gml"));

        var berth = Assert.Single(dataset.Features);
        Assert.Equal("Berth", berth.FeatureType);
        Assert.Equal(GmlGeometryType.Curve, berth.GeometryType);
        var curve = Assert.Single(berth.Curves);
        Assert.Equal(3, curve.Length);
        Assert.Equal(44.6475, curve[0].Latitude, 4);
        Assert.Equal(-63.5713, curve[0].Longitude, 4);
        Assert.Equal("12.5", berth.Attributes["maximumPermittedDraught"]);

        // Complex attribute featureName
        var featureName = Assert.Single(berth.ComplexAttributes, c => c.Code == "featureName");
        Assert.Equal("Berth 23", featureName.SubAttributes["name"]);
    }

    [Fact]
    public void Open_SurfaceDataset_ReturnsClosedRing()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_surface.gml"));

        var area = Assert.Single(dataset.Features);
        Assert.Equal("AnchorageArea", area.FeatureType);
        Assert.Equal(GmlGeometryType.Surface, area.GeometryType);
        Assert.Equal(5, area.ExteriorRing.Length);
        Assert.Empty(area.InteriorRings);
    }

    [Fact]
    public void Open_XlinkDataset_ResolvesReferences()
    {
        var dataset = S131Dataset.Open(GetTestDataPath("harbour_xlink.gml"));

        // Authority and Berth are features; Applicability and ContactDetails are info types
        Assert.Equal(2, dataset.Features.Length);
        Assert.Equal(2, dataset.InformationTypes.Length);

        // Authority is a geometry-less container
        var authority = dataset.Features.Single(f => f.FeatureType == "Authority");
        Assert.Equal(GmlGeometryType.None, authority.GeometryType);
        Assert.Empty(authority.Points);
        Assert.Empty(authority.Curves);
        Assert.Empty(authority.ExteriorRing);

        // Authority should have xlink references
        Assert.True(authority.References.Length >= 1,
            "Authority should have xlink references to info types");

        // Berth feature has geometry
        var berth = dataset.Features.Single(f => f.FeatureType == "Berth");
        Assert.Equal(GmlGeometryType.Point, berth.GeometryType);
        Assert.Single(berth.Points);
    }

    [Fact]
    public void Open_Stream_EquivalentToPath()
    {
        var path = GetTestDataPath("harbour_point.gml");
        var byPath = S131Dataset.Open(path);
        S131Dataset byStream;
        using (var stream = File.OpenRead(path))
        {
            byStream = S131Dataset.Open(stream);
        }

        Assert.Equal(byPath.Features.Length, byStream.Features.Length);
        Assert.Equal(byPath.InformationTypes.Length, byStream.InformationTypes.Length);
        Assert.Equal(byPath.Features[0].FeatureType, byStream.Features[0].FeatureType);
    }
}
