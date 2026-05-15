using System.Linq;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S201.Tests;

public class S201DatasetReaderTests
{
    private static string GetTestDataPath(string filename)
    {
        var basePath = AppContext.BaseDirectory;
        return Path.Combine(basePath, "TestData", filename);
    }

    [Fact]
    public void Open_PointDataset_ReturnsFeaturesAndInfoTypes()
    {
        var dataset = S201Dataset.Open(GetTestDataPath("aton_point.gml"));

        Assert.Equal("S-201", dataset.ProductIdentifier);
        Assert.Equal(2, dataset.Features.Length);
        Assert.Single(dataset.InformationTypes);

        var lateralBuoy = dataset.Features.Single(f => f.FeatureType == "LateralBuoy");
        Assert.Equal("f1", lateralBuoy.Id);
        Assert.Equal(GmlGeometryType.Point, lateralBuoy.GeometryType);
        Assert.Single(lateralBuoy.Points);
        Assert.Equal(36.95, lateralBuoy.Points[0].Latitude, 4);
        Assert.Equal(-76.0133, lateralBuoy.Points[0].Longitude, 4);
        Assert.Equal("1", lateralBuoy.Attributes["categoryOfLateralMark"]);

        Assert.Single(lateralBuoy.InformationReferences);
        Assert.Equal("AtoNStatus", lateralBuoy.InformationReferences[0].Role);
        Assert.Equal("info1", lateralBuoy.InformationReferences[0].InformationRef);
        Assert.Empty(lateralBuoy.FeatureReferences);

        var info = dataset.InformationTypes.Single();
        Assert.Equal("AtonStatusInformation", info.TypeCode);
        Assert.Equal("info1", info.Id);
        Assert.Equal("1", info.Attributes["changeTypes"]);
    }

    [Fact]
    public void Open_PointDataset_ComplexAttributePopulated()
    {
        var dataset = S201Dataset.Open(GetTestDataPath("aton_point.gml"));
        var landmark = dataset.Features.Single(f => f.FeatureType == "Landmark");

        var name = Assert.Single(landmark.ComplexAttributes, c => c.Code == "featureName");
        Assert.Equal("Cape Henry Light", name.SubAttributes["name"]);
    }

    [Fact]
    public void Open_CurveDataset_ReturnsCurveFeature()
    {
        var dataset = S201Dataset.Open(GetTestDataPath("aton_curve.gml"));

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
        var dataset = S201Dataset.Open(GetTestDataPath("aton_surface.gml"));

        var coverage = Assert.Single(dataset.Features);
        Assert.Equal("DataCoverage", coverage.FeatureType);
        Assert.Equal(GmlGeometryType.Surface, coverage.GeometryType);
        Assert.Equal(5, coverage.ExteriorRing.Length);
        Assert.Empty(coverage.InteriorRings);
    }

    [Fact]
    public void Open_XlinkDataset_FeatureReferencesAreSeparatedFromInfoRefs()
    {
        var dataset = S201Dataset.Open(GetTestDataPath("aton_xlink.gml"));

        var equipment = dataset.Features.Single(f => f.Id == "equipment1");
        var parentRef = Assert.Single(equipment.FeatureReferences);
        Assert.Equal("theParentFeature", parentRef.Role);
        Assert.Equal("structure1", parentRef.TargetRef);
        Assert.Empty(equipment.InformationReferences);

        var aggregation = dataset.Features.Single(f => f.FeatureType == "AtonAggregation");
        Assert.Equal(GmlGeometryType.None, aggregation.GeometryType);
        Assert.Equal(2, aggregation.FeatureReferences.Length);
        Assert.All(aggregation.FeatureReferences, r => Assert.Equal("peer", r.Role));
    }

    [Fact]
    public void ResolveReferencedFeatures_WalksXlinkByRoleName()
    {
        var dataset = S201Dataset.Open(GetTestDataPath("aton_xlink.gml"));
        var equipment = dataset.Features.Single(f => f.Id == "equipment1");

        var parents = dataset.ResolveReferencedFeatures(equipment, "theParentFeature").ToList();
        var parent = Assert.Single(parents);
        Assert.Equal("structure1", parent.Id);
        Assert.Equal("LightFloat", parent.FeatureType);
    }
}
