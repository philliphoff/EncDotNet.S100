using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>
/// Tests for <see cref="S421DatasetReader"/> using the IEC S-421 sample
/// route plan datasets.
/// </summary>
public class S421DatasetReaderTests
{
    private const string TestDataDir = "TestData";

    private static S421Dataset Load(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S421Dataset.Open(path);
    }

    // ── Minimal route ────────────────────────────────────────────

    [Fact]
    public void Minimal_ParsesDatasetIdentifier()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        Assert.Equal("S421.TST.GMINI.00001", ds.DatasetIdentifier);
    }

    [Fact]
    public void Minimal_DefaultsProductIdentifier()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        Assert.Equal("S-421", ds.ProductIdentifier);
    }

    [Fact]
    public void Minimal_HasRouteFeature()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var route = ds.Features.Single(f => f.FeatureType == "Route");
        Assert.Equal("RTE", route.Id);
        Assert.Equal(GmlGeometryType.None, route.GeometryType);
    }

    [Fact]
    public void Minimal_RouteHasSimpleAttributes()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var route = ds.Features.Single(f => f.FeatureType == "Route");
        Assert.Equal("1.0", route.Attributes["routeFormatVersion"]);
        Assert.Equal("GMINI.00001", route.Attributes["routeID"]);
        Assert.Equal("1", route.Attributes["routeEditionNo"]);
    }

    [Fact]
    public void Minimal_RouteHasXLinkReferences()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var route = ds.Features.Single(f => f.FeatureType == "Route");
        Assert.Contains(route.References, r => r.Role == "routeInfo" && r.Href == "#RTE.INFO");
        Assert.Contains(route.References, r => r.Role == "routeWaypoints" && r.Href == "#RTE.WPTS");
    }

    [Fact]
    public void Minimal_RouteInfoIsInformationType()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var info = ds.InformationTypes.Single(i => i.TypeCode == "RouteInfo");
        Assert.Equal("RTE.INFO", info.Id);
        Assert.Equal("Basic.Implementation", info.Attributes["routeInfoName"]);
        Assert.Equal("265425000", info.Attributes["routeInfoVesselMMSI"]);
    }

    [Fact]
    public void Minimal_RouteWaypointHasPointGeometry()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var wp = ds.Features.Single(f => f.Id == "RTE.WPT.1");

        Assert.Equal("RouteWaypoint", wp.FeatureType);
        Assert.Equal(GmlGeometryType.Point, wp.GeometryType);
        Assert.Single(wp.Points);
        Assert.Equal(59.892863, wp.Points[0].Latitude, 6);
        Assert.Equal(25.822235, wp.Points[0].Longitude, 6);
    }

    [Fact]
    public void Minimal_RouteWaypointHasAttributes()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var wp = ds.Features.Single(f => f.Id == "RTE.WPT.1");
        Assert.Equal("1", wp.Attributes["routeWaypointID"]);
        Assert.Equal("WP Name 1", wp.Attributes["routeWaypointName"]);
        Assert.Equal("0.7", wp.Attributes["routeWaypointTurnRadius"]);
    }

    [Fact]
    public void Minimal_RouteWaypointsContainerHasReferences()
    {
        var ds = Load("RTE-TEST-GMIN.s421.gml");
        var wpts = ds.Features.Single(f => f.FeatureType == "RouteWaypoints");
        var waypointRefs = wpts.References.Where(r => r.Role == "routeWaypoint").ToList();
        Assert.Equal(2, waypointRefs.Count);
        Assert.Equal("#RTE.WPT.1", waypointRefs[0].Href);
        Assert.Equal("#RTE.WPT.10", waypointRefs[1].Href);
    }

    // ── Basic route ──────────────────────────────────────────────

    [Fact]
    public void Basic_ParsesAllFeatures()
    {
        var ds = Load("RTE-TEST-GBASIC.s421.gml");
        Assert.NotEmpty(ds.Features);
        Assert.Contains(ds.Features, f => f.FeatureType == "Route");
        Assert.Contains(ds.Features, f => f.FeatureType == "RouteWaypoints");
        Assert.Contains(ds.Features, f => f.FeatureType == "RouteWaypoint");
    }

    // ── Full route ───────────────────────────────────────────────

    [Fact]
    public void Full_ParsesAllFeatures()
    {
        var ds = Load("RTE-TEST-GFULL.s421.gml");
        Assert.Equal("S421.TST.GFULL.00001", ds.DatasetIdentifier);
        Assert.NotEmpty(ds.Features);
        Assert.NotEmpty(ds.InformationTypes);
    }

    [Fact]
    public void Full_RouteInfoHasFullVesselMetadata()
    {
        var ds = Load("RTE-TEST-GFULL.s421.gml");
        var info = ds.InformationTypes.Single(i => i.TypeCode == "RouteInfo");
        Assert.Equal("BALTIC BRIGHT", info.Attributes["routeInfoVesselName"]);
        Assert.Equal("9129263", info.Attributes["routeInfoVesselIMO"]);
        Assert.Equal("SIHZ", info.Attributes["routeInfoVesselCallsign"]);
    }
}
