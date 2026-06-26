using System;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class RouteEditToolTests
{
    [Fact]
    public void Constructor_NullRoutes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RouteEditTool(null!));
    }

    [Fact]
    public void Id_IsStable()
    {
        var tool = new RouteEditTool(new RoutesService());
        Assert.Equal(RouteEditTool.ToolId, tool.Id);
    }

    [Fact]
    public void FormatSummary_NoActiveRoute_ReturnsNoDataText()
    {
        var tool = new RouteEditTool(new RoutesService());
        Assert.Equal(Strings.Status_RouteEditNoData, tool.FormatSummary());
    }

    [Fact]
    public void FormatSummary_SingleWaypoint_ReturnsNoDataText()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));

        var tool = new RouteEditTool(svc);
        Assert.Equal(Strings.Status_RouteEditNoData, tool.FormatSummary());
    }

    [Fact]
    public void FormatSummary_TwoWaypoints_IncludesTotalDistance()
    {
        var svc = new RoutesService();
        var route = svc.Routes.CreateRoute("R1");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 1));

        var tool = new RouteEditTool(svc);
        var summary = tool.FormatSummary();

        Assert.NotNull(summary);
        Assert.NotEqual(Strings.Status_RouteEditNoData, summary);
        // One degree of longitude on the equator ≈ 60 NM.
        Assert.Contains("60", summary);
    }
}
