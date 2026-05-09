using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S124.Tests;

/// <summary>
/// Tests for <see cref="GmlFeatureXmlSource{TFeature}"/> with S-124 features,
/// verifying the Part 9 FeatureXML intermediate format produced for XSLT
/// portrayal rules.
/// </summary>
public class S124FeatureXmlSourceTests
{
    private const string TestDataDir = "TestData";

    private static S124Dataset LoadTestData(string fileName) =>
        S124Dataset.Open(Path.Combine(TestDataDir, fileName));

    private static XDocument GetFeatureXmlDoc(S124Dataset dataset)
    {
        var source = new GmlFeatureXmlSource<S124Feature>(dataset.Features);
        using var reader = source.GetFeatureXml();
        return XDocument.Load(reader);
    }

    // ── Structure tests ──────────────────────────────────────────

    [Fact]
    public void FeatureXml_HasDatasetRoot()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        Assert.NotNull(doc.Root);
        Assert.Equal("Dataset", doc.Root.Name.LocalName);
    }

    [Fact]
    public void FeatureXml_HasPointsAndFeaturesElements()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        Assert.NotNull(doc.Root!.Element("Points"));
        Assert.NotNull(doc.Root!.Element("Features"));
    }

    // ── Point features ───────────────────────────────────────────

    [Fact]
    public void FeatureXml_PointFeature_HasPrimitiveAttribute()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var navwarnPart = features.First(e => e.Name.LocalName == "NavwarnPart");

        Assert.Equal("Point", navwarnPart.Attribute("primitive")?.Value);
    }

    [Fact]
    public void FeatureXml_PointFeature_HasIdAttribute()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var navwarnPart = features.First(e => e.Name.LocalName == "NavwarnPart");

        Assert.NotNull(navwarnPart.Attribute("id")?.Value);
        Assert.NotEmpty(navwarnPart.Attribute("id")!.Value);
    }

    [Fact]
    public void FeatureXml_PointFeature_HasPointReference()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var navwarnPart = features.First(e => e.Name.LocalName == "NavwarnPart");

        var pointRef = navwarnPart.Element("Point");
        Assert.NotNull(pointRef);
        Assert.NotNull(pointRef.Attribute("ref")?.Value);
    }

    [Fact]
    public void FeatureXml_PointsRegistry_ContainsReferencedPoints()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var points = doc.Root!.Element("Points")!.Elements("Point").ToList();
        Assert.True(points.Count >= 3, $"Expected at least 3 points in registry, got {points.Count}");

        // Verify each point has id, lat, lon
        foreach (var point in points)
        {
            Assert.NotNull(point.Attribute("id")?.Value);
            Assert.NotNull(point.Attribute("lat")?.Value);
            Assert.NotNull(point.Attribute("lon")?.Value);
        }
    }

    [Fact]
    public void FeatureXml_PointReference_ResolvesToPointInRegistry()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var navwarnPart = features.First(e => e.Name.LocalName == "NavwarnPart");
        var pointRefId = navwarnPart.Element("Point")!.Attribute("ref")!.Value;

        var points = doc.Root!.Element("Points")!.Elements("Point").ToList();
        var registeredPoint = points.FirstOrDefault(p => p.Attribute("id")?.Value == pointRefId);
        Assert.NotNull(registeredPoint);
    }

    // ── Curve features ───────────────────────────────────────────

    [Fact]
    public void FeatureXml_CurveFeature_HasCurvePrimitive()
    {
        var ds = LoadTestData("navwarn_curve.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var curveFeat = features.First(e => e.Attribute("primitive")?.Value == "Curve");

        Assert.Equal("NavwarnPart", curveFeat.Name.LocalName);
    }

    [Fact]
    public void FeatureXml_CurveFeature_HasMultiplePointReferences()
    {
        var ds = LoadTestData("navwarn_curve.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var curveFeat = features.First(e => e.Attribute("id")?.Value == "f1");
        var pointRefs = curveFeat.Elements("Point").ToList();

        Assert.Equal(4, pointRefs.Count); // 4 coordinates in the curve
    }

    // ── Surface features ─────────────────────────────────────────

    [Fact]
    public void FeatureXml_SurfaceFeature_HasSurfacePrimitive()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var surfaceFeat = features.First(e => e.Attribute("primitive")?.Value == "Surface");

        Assert.Equal("NavwarnAreaAffected", surfaceFeat.Name.LocalName);
    }

    [Fact]
    public void FeatureXml_SurfaceFeature_HasExteriorRingPoints()
    {
        var ds = LoadTestData("navwarn_surface.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var surfaceFeat = features.First(e => e.Attribute("id")?.Value == "f1");
        var pointRefs = surfaceFeat.Elements("Point").ToList();

        Assert.Equal(5, pointRefs.Count); // 5 points in exterior ring (closed)
    }

    // ── Attributes ───────────────────────────────────────────────

    [Fact]
    public void FeatureXml_SimpleAttribute_IncludedAsChildElement()
    {
        var ds = LoadTestData("navwarn_point.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var navwarnPart = features.First(e => e.Name.LocalName == "NavwarnPart");

        var restriction = navwarnPart.Element("restriction");
        Assert.NotNull(restriction);
        Assert.Equal("7", restriction.Value);
    }

    // ── FeatureTypesPresent ──────────────────────────────────────

    [Fact]
    public void FeatureTypesPresent_MixedDataset_ReturnsAllTypes()
    {
        var ds = LoadTestData("navwarn_mixed.gml");
        var source = new GmlFeatureXmlSource<S124Feature>(ds.Features);

        var types = source.FeatureTypesPresent;
        Assert.Contains("NavwarnPart", types);
        Assert.Contains("NavwarnAreaAffected", types);
        Assert.Contains("TextPlacement", types);
    }

    // ── Mixed dataset ────────────────────────────────────────────

    [Fact]
    public void FeatureXml_MixedDataset_HasAllFeatures()
    {
        var ds = LoadTestData("navwarn_mixed.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        Assert.Equal(5, features.Count);
    }

    [Fact]
    public void FeatureXml_NoGeometryFeature_HasNoGeometryPrimitive()
    {
        var ds = LoadTestData("navwarn_mixed.gml");
        var doc = GetFeatureXmlDoc(ds);

        var features = doc.Root!.Element("Features")!.Elements().ToList();
        var noGeom = features.First(e => e.Attribute("id")?.Value == "f5");

        Assert.Equal("NoGeometry", noGeom.Attribute("primitive")?.Value);
        Assert.Empty(noGeom.Elements("Point"));
    }
}
