using EncDotNet.S100.Datasets.S129.Fusion.Tests.Helpers;

namespace EncDotNet.S100.Datasets.S129.Fusion.Tests;

public class CrossProductResolverTests
{
    [Fact]
    public void Resolve_NoCandidates_ProducesUnresolvedForEachExpectedKind()
    {
        var plan = SyntheticDatasets.MakePlan();
        var result = S129CrossProductResolver.Resolve(plan);

        Assert.Null(result.Bathymetry);
        Assert.Null(result.WaterLevel);
        Assert.Null(result.Route);
        Assert.Equal(3, result.Unresolved.Length);
        Assert.All(result.Unresolved,
            u => Assert.Equal(S129ReferenceResolutionReason.DatasetNotProvided, u.Reason));
    }

    [Fact]
    public void Resolve_MatchingRoute_BindsByRouteId()
    {
        var plan = SyntheticDatasets.MakePlan(sourceRouteName: "ROUTE_A", sourceRouteVersion: "1");
        var route = SyntheticDatasets.MakeRoute(routeId: "ROUTE_A", editionNumber: 1);

        var result = S129CrossProductResolver.Resolve(plan, route: route);

        Assert.NotNull(result.Route);
        Assert.Same(route, result.Route!.Value);
        Assert.DoesNotContain(result.Unresolved, u => u.ExpectedKind == "S-421 route");
    }

    [Fact]
    public void Resolve_RouteIdentifierMismatch_LeavesRouteUnresolved()
    {
        var plan = SyntheticDatasets.MakePlan(sourceRouteName: "ROUTE_A");
        var route = SyntheticDatasets.MakeRoute(routeId: "ROUTE_B");

        var result = S129CrossProductResolver.Resolve(plan, route: route);

        Assert.Null(result.Route);
        Assert.Contains(result.Unresolved,
            u => u.ExpectedKind == "S-421 route" &&
                 u.Reason == S129ReferenceResolutionReason.IdentifierMismatch);
    }

    [Fact]
    public void Resolve_RouteVersionMismatch_LeavesRouteUnresolved()
    {
        var plan = SyntheticDatasets.MakePlan(sourceRouteName: "ROUTE_A", sourceRouteVersion: "2");
        var route = SyntheticDatasets.MakeRoute(routeId: "ROUTE_A", editionNumber: 1);

        var result = S129CrossProductResolver.Resolve(plan, route: route);

        Assert.Null(result.Route);
        Assert.Contains(result.Unresolved,
            u => u.Reason == S129ReferenceResolutionReason.IdentifierMismatch);
    }

    [Fact]
    public void Resolve_PlanWithoutSourceRoute_BindsCandidateRouteSynthetically()
    {
        var plan = SyntheticDatasets.MakePlan(sourceRouteName: null, sourceRouteVersion: null);
        var route = SyntheticDatasets.MakeRoute();

        var result = S129CrossProductResolver.Resolve(plan, route: route);

        Assert.NotNull(result.Route);
        Assert.Equal("ROUTE_A", result.Route!.ExternalReference.Identifier);
    }

    [Fact]
    public void Resolve_BathymetryProvided_AlwaysResolves()
    {
        var plan = SyntheticDatasets.MakePlan();
        var bathy = SyntheticDatasets.MakeBathymetry();

        var result = S129CrossProductResolver.Resolve(plan, bathymetry: bathy);

        Assert.NotNull(result.Bathymetry);
        Assert.Same(bathy, result.Bathymetry!.Value);
    }

    [Fact]
    public void Resolve_WaterLevelProvided_AlwaysResolves()
    {
        var plan = SyntheticDatasets.MakePlan();
        var wl = SyntheticDatasets.MakeWaterLevel();

        var result = S129CrossProductResolver.Resolve(plan, waterLevel: wl);

        Assert.NotNull(result.WaterLevel);
        Assert.Same(wl, result.WaterLevel!.Value);
    }
}
