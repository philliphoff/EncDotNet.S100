using System.IO;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// End-to-end checks that the S-125 and S-421 dataset processors lift
/// xlink-style references out of the source GML and surface them as
/// first-class <see cref="FeatureReference"/> entries on
/// <see cref="FeatureInfo"/>. Exercises the milestone-3 promotion of
/// per-spec reference shapes (<c>S125InformationReference</c>,
/// <c>S421Reference</c>) into the uniform pick contract.
/// </summary>
public class FeatureReferenceIntegrationTests
{
    private static string TestData(params string[] parts)
        => Path.Combine(new[] { AppContext.BaseDirectory, "TestData" }.Concat(parts).ToArray());

    private static PortrayalCatalogueManager CreateCatalogueManager(string spec)
    {
        var manager = new PortrayalCatalogueManager();
        manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        return manager;
    }

    [Fact]
    public void S125Processor_LiftsAtonStatusBindingIntoReferences()
    {
        var path = TestData("S125", "aton_point.gml");
        Assert.True(File.Exists(path), $"Missing fixture: {path}");

        var processor = new S125DatasetProcessor(path, CreateCatalogueManager("S-125"));
        // f1 = LateralBuoy with <S125:AtoNStatus xlink:href="#info1"/>
        var info = processor.GetFeatureInfo("f1");

        Assert.NotNull(info);
        Assert.Equal("LateralBuoy", info!.FeatureType);
        Assert.Single(info.References);
        var reference = info.References[0];
        Assert.Equal("AtoNStatus", reference.Role);
        Assert.Equal("info1", reference.TargetRef);
    }

    [Fact]
    public void S125Processor_DoesNotLeakReferencesIntoAttributes()
    {
        var path = TestData("S125", "aton_point.gml");
        var processor = new S125DatasetProcessor(path, CreateCatalogueManager("S-125"));

        var info = processor.GetFeatureInfo("f1");

        Assert.NotNull(info);
        Assert.DoesNotContain(info!.Attributes, a => a.Code.EndsWith(".ref", System.StringComparison.Ordinal));
    }

    [Fact]
    public void S421Processor_LiftsRouteTopologyIntoReferences()
    {
        var path = TestData("S421", "RTE-TEST-GMIN.s421.gml");
        Assert.True(File.Exists(path), $"Missing fixture: {path}");

        var processor = new S421DatasetProcessor(path, CreateCatalogueManager("S-421"));
        // RTE = Route, points at routeInfo / routeWaypoints / routeWaypointsCollection.
        var info = processor.GetFeatureInfo("RTE");

        Assert.NotNull(info);
        Assert.Equal("Route", info!.FeatureType);
        Assert.NotEmpty(info.References);

        var waypoints = info.References.FirstOrDefault(r => r.Role == "routeWaypoints");
        Assert.NotNull(waypoints);
        Assert.Equal("RTE.WPTS", waypoints!.TargetRef);
        Assert.Equal("http://www.iho.int/S-421/gml/1.0/roles/routeWaypoints", waypoints.ArcRole);
    }

    [Fact]
    public void S421Processor_LiftsWaypointBackReferenceIntoReferences()
    {
        var path = TestData("S421", "RTE-TEST-GMIN.s421.gml");
        var processor = new S421DatasetProcessor(path, CreateCatalogueManager("S-421"));

        // RTE.WPT.1 = first RouteWaypoint, contains <routeWaypointCollection
        // xlink:href="#RTE.WPTS"/> + neighbour leg references.
        var info = processor.GetFeatureInfo("RTE.WPT.1");

        Assert.NotNull(info);
        Assert.Equal("RouteWaypoint", info!.FeatureType);
        Assert.Contains(info.References, r =>
            r.Role == "routeWaypointCollection" && r.TargetRef == "RTE.WPTS");
    }

    [Fact]
    public void S421Processor_DoesNotLeakReferencesIntoAttributes()
    {
        var path = TestData("S421", "RTE-TEST-GMIN.s421.gml");
        var processor = new S421DatasetProcessor(path, CreateCatalogueManager("S-421"));

        var info = processor.GetFeatureInfo("RTE");

        Assert.NotNull(info);
        Assert.DoesNotContain(info!.Attributes, a => a.Code.StartsWith("→", System.StringComparison.Ordinal));
    }
}
