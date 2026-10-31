using System.ComponentModel;
using System.Reflection;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>check_route</c> without charts: every track leaves chart coverage, so
/// the tool's routing, corridor handling and projection show without
/// fixture cells. The land and drying checks run on generated cells in
/// the viewer tests (<c>RouteCheckFindingsTests</c>) and the scenario tests.
/// </summary>
public class CheckRouteToolTests
{
    private static readonly GeoPolyline Polyline = new([new GeoPoint(0, 0), new GeoPoint(0, 0.01), new GeoPoint(0.01, 0.01)]);

    private static async Task<CheckRouteResult> Ok(CheckRouteTool tool, CheckRouteRequest request)
    {
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static async Task<ToolError> Err(CheckRouteTool tool, CheckRouteRequest request)
    {
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetError(out var error));
        return error!;
    }

    [Fact]
    public async Task A_polyline_off_every_chart_leaves_coverage_on_each_leg()
    {
        var value = await Ok(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest(Polyline: Polyline));

        Assert.Null(value.RouteId);
        Assert.Equal(2, value.Summary.Count);
        Assert.Equal(0, value.Summary.MustReview);
        Assert.Equal(2, value.Summary.Check);
        Assert.Equal("2 to check", value.Summary.Text);
        Assert.All(value.Findings, f =>
        {
            Assert.Equal(RouteFindingCodes.OutsideChartCoverage.Value, f.Code);
            Assert.Equal("check", f.Group);
            Assert.Equal("data", f.Category);
            Assert.Equal("span", f.Location!.Kind);
            Assert.Null(f.Feature);
        });
        Assert.Equal([0, 1], value.Findings.Select(f => f.LegIndex!.Value));
        Assert.Equal("data.outside_chart_coverage.0", value.Findings[0].Id);
        Assert.Equal("Leg 1 → 2 · 0.60 NM", value.Findings[0].Meta);
        Assert.Equal("check", value.Verdict.Kind);
        Assert.Equal("data.outside_chart_coverage.0", value.Verdict.HeadlineId);
        Assert.Null(value.Clearance);
        Assert.InRange(value.DistanceNm, 1.19, 1.21);
        Assert.Empty(value.Datasets);
        Assert.StartsWith("Not for navigation", value.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_corridor_half_width_applies_to_both_sides()
    {
        var value = await Ok(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest(Polyline: Polyline, CorridorHalfWidthMeters: 120));

        Assert.All(value.Legs, l =>
        {
            Assert.Equal(120, l.PortMeters);
            Assert.Equal(120, l.StarboardMeters);
        });
    }

    [Fact]
    public async Task A_polylines_own_corridor_width_is_a_half_width()
    {
        var value = await Ok(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest(Polyline: Polyline with { CorridorWidthMeters = 80 }));

        Assert.Equal(80, value.Legs[0].PortMeters);
        Assert.Equal(80, value.Legs[0].StarboardMeters);
    }

    [Fact]
    public async Task A_route_uses_each_legs_cross_track_limits()
    {
        var route = new PassageRouteSnapshot("r1", "Test route",
        [
            new PassageRouteLeg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)], LegGeometry.Rhumb, null, null, null, null, "Leg 1 → 2", 30, 70),
            new PassageRouteLeg([new GeoPosition(0, 0.01), new GeoPosition(0.01, 0.01)], LegGeometry.Rhumb, null, null, null, null, "Leg 2 → 3"),
        ], DateTimeOffset.UnixEpoch);
        var routes = new FakeRoutes(route);

        var value = await Ok(new CheckRouteTool(new FakeDatasetCatalog(), routes), new CheckRouteRequest(RouteId: "r1"));

        Assert.Equal("r1", routes.LastRouteId);
        Assert.Equal("r1", value.RouteId);
        Assert.Equal("Test route", value.RouteName);
        Assert.Equal((30.0, 70.0), (value.Legs[0].PortMeters, value.Legs[0].StarboardMeters));
        Assert.Equal((50.0, 50.0), (value.Legs[1].PortMeters, value.Legs[1].StarboardMeters));
    }

    [Fact]
    public async Task Without_host_routes_a_polyline_is_required()
    {
        var error = await Err(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest());

        Assert.Equal("invalid_argument", error.Code);
    }

    [Fact]
    public async Task Route_lookup_errors_pass_through()
    {
        var error = await Err(new CheckRouteTool(new FakeDatasetCatalog(), new FakeRoutes(null)), new CheckRouteRequest(RouteId: "nope"));

        Assert.Equal("invalid_argument", error.Code);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public async Task A_negative_half_width_is_rejected(double half)
    {
        var error = await Err(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest(Polyline: Polyline, CorridorHalfWidthMeters: half));

        Assert.Equal("invalid_argument", error.Code);
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(null, double.NaN)]
    public async Task A_negative_track_clearance_or_near_band_is_rejected(double? clearance, double? nearby)
    {
        var error = await Err(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest(Polyline: Polyline, TrackClearanceMeters: clearance, NearbyDistanceMeters: nearby));

        Assert.Equal("invalid_argument", error.Code);
    }

    [Fact]
    public void A_hazards_details_carry_its_class_and_position()
    {
        var details = CheckRouteDetails.From(new HazardDetails
        {
            FeatureType = "VirtualAISAidToNavigation",
            HazardClass = RouteHazardClass.AidToNavigation,
            Position = RouteFindingPosition.OnTrack,
            Side = RouteFindingSide.Starboard,
            Offset = EncDotNet.S100.Quantities.Length.FromMetres(12.34),
        })!.Hazard!;

        Assert.Equal(new CheckRouteHazardDetails("VirtualAISAidToNavigation", "aid_to_navigation", "on_track", "starboard", 12.3), details);
    }

    [Fact]
    public async Task The_safety_contour_and_depth_go_together()
    {
        var error = await Err(new CheckRouteTool(new FakeDatasetCatalog()), new CheckRouteRequest(Polyline: Polyline, SafetyContourMeters: 10));

        Assert.Equal("invalid_argument", error.Code);
    }

    [Fact]
    public async Task With_depths_and_no_charts_only_missing_data_is_found()
    {
        var value = await Ok(new CheckRouteTool(new FakeDatasetCatalog()),
            new CheckRouteRequest(Polyline: Polyline, SafetyContourMeters: 10, SafetyDepthMeters: 8, DraftMeters: 5));

        // The draft turns on the clearance check, which finds no depth or water level either.
        Assert.Equal(
            [RouteFindingCodes.NoChartedDepth.Value, RouteFindingCodes.NoWaterLevelCoverage.Value, RouteFindingCodes.OutsideChartCoverage.Value],
            value.Findings.Select(f => f.Code).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal("ran", value.Checks.Single(c => c.Name == "chart").State);
    }

    [Theory]
    [InlineData(2, 5, "2 to review")]
    [InlineData(0, 1, "1 to check")]
    [InlineData(0, 0, "No findings for your settings")]
    public void The_summary_names_the_worst_group(int mustReview, int check, string expected)
    {
        Assert.Equal(expected, CheckRouteResult.SummaryText(mustReview, check));
    }

    // The contract suite picks up *Request / *Result / *Summary records;
    // every other CheckRoute* record is checked here.
    [Fact]
    public void Every_check_route_property_has_a_description()
    {
        var types = typeof(CheckRouteResult).Assembly.GetTypes()
            .Where(t => t.IsPublic && t.Name.StartsWith("CheckRoute", StringComparison.Ordinal) && t != typeof(CheckRouteTool))
            .ToList();
        Assert.Contains(typeof(CheckRouteClearance), types);

        var missing = types
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), (t, p) => (t, p))
            .Where(x => string.IsNullOrWhiteSpace(x.p.GetCustomAttribute<DescriptionAttribute>()?.Description))
            .Select(x => $"{x.t.Name}.{x.p.Name}")
            .ToList();

        Assert.Empty(missing);
    }

    private sealed class FakeRoutes(PassageRouteSnapshot? route) : IPassageRouteProvider
    {
        public string? LastRouteId { get; private set; }

        public Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken)
        {
            LastRouteId = routeId;
            return Task.FromResult(route is null
                ? ToolResult<PassageRouteSnapshot>.Err(new InvalidArgument("routeId", "no such route"))
                : ToolResult<PassageRouteSnapshot>.Ok(route));
        }
    }
}
