using System.Xml.Linq;
using EncDotNet.S100.Datasets.S421;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>
/// Tests for <see cref="S421FeatureXmlSource"/> verifying the Part 9
/// FeatureXML intermediate format produced for XSLT portrayal rules.
/// </summary>
public class S421FeatureXmlSourceTests
{
    private const string TestDataDir = "TestData";

    private static S421Dataset Load(string fileName) =>
        S421Dataset.Open(Path.Combine(TestDataDir, fileName));

    private static XDocument GetFeatureXmlDoc(S421Dataset dataset)
    {
        var source = new S421FeatureXmlSource(dataset);
        using var reader = source.GetFeatureXml();
        return XDocument.Load(reader);
    }

    [Fact]
    public void HasDatasetRootWithPointsAndFeatures()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var doc = GetFeatureXmlDoc(ds);

        Assert.NotNull(doc.Root);
        Assert.Equal("Dataset", doc.Root!.Name.LocalName);
        Assert.NotNull(doc.Root.Element("Points"));
        Assert.NotNull(doc.Root.Element("Features"));
    }

    [Fact]
    public void RouteWaypoint_EmittedWithPointPrimitive()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var doc = GetFeatureXmlDoc(ds);

        var wp = doc.Root!.Element("Features")!
            .Elements("RouteWaypoint")
            .First(e => e.Attribute("id")?.Value == "RTE.WPT.1");

        Assert.Equal("Point", wp.Attribute("primitive")?.Value);
        var pointRef = wp.Element("Point");
        Assert.NotNull(pointRef);
        Assert.NotNull(pointRef.Attribute("ref")?.Value);
    }

    [Fact]
    public void Route_EmittedWithNoGeometryPrimitive()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var doc = GetFeatureXmlDoc(ds);

        var route = doc.Root!.Element("Features")!
            .Elements("Route").Single();

        Assert.Equal("NoGeometry", route.Attribute("primitive")?.Value);
    }

    [Fact]
    public void PointReference_ResolvesToRegistry()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var doc = GetFeatureXmlDoc(ds);

        var wp = doc.Root!.Element("Features")!
            .Elements("RouteWaypoint")
            .First(e => e.Attribute("id")?.Value == "RTE.WPT.1");
        var refId = wp.Element("Point")!.Attribute("ref")!.Value;

        var registered = doc.Root.Element("Points")!
            .Elements("Point")
            .FirstOrDefault(p => p.Attribute("id")?.Value == refId);
        Assert.NotNull(registered);
        Assert.NotNull(registered.Attribute("lat")?.Value);
        Assert.NotNull(registered.Attribute("lon")?.Value);
    }

    [Fact]
    public void SimpleAttributes_EmittedAsChildElements()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var doc = GetFeatureXmlDoc(ds);

        var wp = doc.Root!.Element("Features")!
            .Elements("RouteWaypoint")
            .First(e => e.Attribute("id")?.Value == "RTE.WPT.1");

        Assert.Equal("1", wp.Element("routeWaypointID")?.Value);
        Assert.Equal("WP Name 1", wp.Element("routeWaypointName")?.Value);
    }

    [Fact]
    public void FeatureTypesPresent_IncludesAllFeatureTypes()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var source = new S421FeatureXmlSource(ds);

        var types = source.FeatureTypesPresent;
        Assert.Contains("Route", types);
        Assert.Contains("RouteWaypoint", types);
        Assert.Contains("RouteWaypoints", types);
    }
}
