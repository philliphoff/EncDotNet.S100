using System.Xml.Linq;
using EncDotNet.S100.Datasets.S122;

namespace EncDotNet.S100.Datasets.S122.Tests;

/// <summary>
/// Tests for <see cref="S122FeatureXmlSource"/> verifying the Part 9 FeatureXML
/// intermediate format produced for XSLT portrayal rules.
/// </summary>
public class S122FeatureXmlSourceTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "122TESTDATASET.gml";

    private static S122Dataset LoadSample() =>
        S122Dataset.Open(Path.Combine(TestDataDir, SampleFile));

    private static XDocument GetFeatureXmlDoc(S122Dataset dataset)
    {
        var source = new S122FeatureXmlSource(dataset);
        using var reader = source.GetFeatureXml();
        return XDocument.Load(reader);
    }

    [Fact]
    public void FeatureXml_HasDatasetRoot()
    {
        var doc = GetFeatureXmlDoc(LoadSample());

        Assert.NotNull(doc.Root);
        Assert.Equal("Dataset", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void FeatureXml_HasPointsAndFeaturesElements()
    {
        var doc = GetFeatureXmlDoc(LoadSample());

        Assert.NotNull(doc.Root!.Element("Points"));
        Assert.NotNull(doc.Root!.Element("Features"));
    }

    [Fact]
    public void FeatureXml_EmitsAllFeaturesWithIdAndPrimitive()
    {
        var doc = GetFeatureXmlDoc(LoadSample());
        var features = doc.Root!.Element("Features")!.Elements().ToList();

        Assert.Equal(4, features.Count);
        foreach (var f in features)
        {
            Assert.NotNull(f.Attribute("id"));
            Assert.NotNull(f.Attribute("primitive"));
        }
    }

    [Fact]
    public void FeatureXml_SurfaceFeature_EmitsExteriorRingPointRefs()
    {
        var doc = GetFeatureXmlDoc(LoadSample());
        var f = doc.Root!.Element("Features")!.Elements()
            .First(e => (string?)e.Attribute("id") == "FEATURE_ID_0003");

        Assert.Equal("Surface", (string?)f.Attribute("primitive"));
        // Exterior ring has 5 points; FeatureXmlSource emits a <Point ref=…/> per coord.
        var refs = f.Elements("Point").Count();
        Assert.Equal(5, refs);
    }

    [Fact]
    public void FeatureXml_CurveFeature_EmitsAllCurvePointRefs()
    {
        var doc = GetFeatureXmlDoc(LoadSample());
        var f = doc.Root!.Element("Features")!.Elements()
            .First(e => (string?)e.Attribute("id") == "FEATURE_ID_0004");

        Assert.Equal("Curve", (string?)f.Attribute("primitive"));
        Assert.Equal(4, f.Elements("Point").Count());
    }

    [Fact]
    public void FeatureTypesPresent_ReportsExpectedSet()
    {
        var source = new S122FeatureXmlSource(LoadSample());
        var types = source.FeatureTypesPresent.ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("MarineProtectedArea", types);
        Assert.Contains("RestrictedArea", types);
        Assert.Contains("VesselTrafficServiceArea", types);
    }
}
