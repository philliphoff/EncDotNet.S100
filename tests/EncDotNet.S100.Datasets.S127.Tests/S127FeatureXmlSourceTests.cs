using System.Xml.Linq;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S127.Tests;

/// <summary>
/// Tests for <see cref="GmlFeatureXmlSource{TFeature}"/> with S-127 features,
/// verifying the Part 9 FeatureXML intermediate format used by S-127 XSLT
/// portrayal rules.
/// </summary>
public class S127FeatureXmlSourceTests
{
    private const string TestDataDir = "TestData";

    private static S127Dataset LoadTestData(string fileName) =>
        S127Dataset.Open(Path.Combine(TestDataDir, fileName));

    private static XDocument GetFeatureXmlDoc(S127Dataset dataset)
    {
        var source = new GmlFeatureXmlSource<S127Feature>(dataset.Features);
        using var reader = source.GetFeatureXml();
        return XDocument.Load(reader);
    }

    [Fact]
    public void FeatureXml_HasDatasetRoot()
    {
        var ds = LoadTestData("marine_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        Assert.NotNull(doc.Root);
        Assert.Equal("Dataset", doc.Root.Name.LocalName);
    }

    [Fact]
    public void FeatureXml_HasPointsAndFeaturesElements()
    {
        var ds = LoadTestData("marine_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        Assert.NotNull(doc.Root!.Element("Points"));
        Assert.NotNull(doc.Root!.Element("Features"));
    }

    [Fact]
    public void FeatureXml_PointFeature_HasPrimitiveAttribute()
    {
        var ds = LoadTestData("marine_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var pbp = features.First(e => e.Name.LocalName == "PilotBoardingPlace");

        Assert.Equal("Point", pbp.Attribute("primitive")?.Value);
    }

    [Fact]
    public void FeatureXml_PointFeature_HasPointReference()
    {
        var ds = LoadTestData("marine_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var pbp = features.First(e => e.Name.LocalName == "PilotBoardingPlace");

        var pointRef = pbp.Element("Point");
        Assert.NotNull(pointRef);
        Assert.NotNull(pointRef.Attribute("ref")?.Value);
    }

    [Fact]
    public void FeatureXml_PointReference_ResolvesToPointInRegistry()
    {
        var ds = LoadTestData("marine_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var pbp = features.First(e => e.Name.LocalName == "PilotBoardingPlace");
        var pointRefId = pbp.Element("Point")!.Attribute("ref")!.Value;

        var points = doc.Root!.Element("Points")!.Elements("Point").ToList();
        var registered = points.FirstOrDefault(p => p.Attribute("id")?.Value == pointRefId);
        Assert.NotNull(registered);
    }

    [Fact]
    public void FeatureXml_CurveFeature_HasMultiplePointReferences()
    {
        var ds = LoadTestData("marine_curve.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var curveFeat = features.First(e => e.Attribute("id")?.Value == "f1");
        var pointRefs = curveFeat.Elements("Point").ToList();

        Assert.Equal(4, pointRefs.Count);
        Assert.Equal("Curve", curveFeat.Attribute("primitive")?.Value);
    }

    [Fact]
    public void FeatureXml_SurfaceFeature_HasExteriorRingPoints()
    {
        var ds = LoadTestData("marine_surface.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var surface = features.First(e => e.Attribute("id")?.Value == "f1");

        Assert.Equal("Surface", surface.Attribute("primitive")?.Value);
        Assert.Equal(5, surface.Elements("Point").Count());
    }

    [Fact]
    public void FeatureXml_SimpleAttribute_IncludedAsChildElement()
    {
        var ds = LoadTestData("marine_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var pbp = features.First(e => e.Name.LocalName == "PilotBoardingPlace"
            && e.Attribute("id")?.Value == "f1");

        var category = pbp.Element("categoryOfPilotBoardingPlace");
        Assert.NotNull(category);
        Assert.Equal("1", category.Value);
    }

    [Fact]
    public void FeatureTypesPresent_MixedDataset_ReturnsAllTypes()
    {
        var ds = LoadTestData("marine_mixed.gml");
        var source = new GmlFeatureXmlSource<S127Feature>(ds.Features);

        var types = source.FeatureTypesPresent;
        Assert.Contains("PilotBoardingPlace", types);
        Assert.Contains("RouteingMeasure", types);
        Assert.Contains("RestrictedArea", types);
        Assert.Contains("SignalStationTraffic", types);
        Assert.Contains("Authority", types);
    }

    [Fact]
    public void FeatureXml_NoGeometryFeature_HasNoGeometryPrimitive()
    {
        var ds = LoadTestData("marine_mixed.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var noGeom = features.First(e => e.Attribute("id")?.Value == "f5");

        Assert.Equal("NoGeometry", noGeom.Attribute("primitive")?.Value);
        Assert.Empty(noGeom.Elements("Point"));
    }
}
