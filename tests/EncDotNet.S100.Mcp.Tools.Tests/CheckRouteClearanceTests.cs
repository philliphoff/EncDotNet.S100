using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>check_route</c>'s clearance on synthetic S-102 + S-104 grids: a 4 × 4 grid
/// of 0,01° cells from (0, 0), 12,5 m deep, with water levels of 1,5 / 1,6 /
/// 1,7 m at 00:00 / 01:00 / 02:00 on 1 January 2024, both on LAT.
/// </summary>
public class CheckRouteClearanceTests
{
    private const int Lat = 23;
    private static readonly DateTimeOffset Midnight = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // About 1,4 NM due east, inside the S-104 node extent (0–0,03°).
    private static readonly GeoPolyline Polyline = new([new GeoPoint(0.005, 0.005), new GeoPoint(0.005, 0.028)]);

    private static FakeDatasetCatalog Catalog(Func<int, int, float>? depthAt = null, int? depthDatum = Lat)
    {
        var bathymetry = S102Synth.Dataset(depthAt: depthAt);
        var water = S104Synth.Dataset();
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("102A", source: new S102CoverageSource(
            new S102Dataset { HorizontalCRS = 4326, VerticalDatum = depthDatum, Coverages = bathymetry.Coverages })));
        catalog.Add(LoadedDatasetFactory.S104("104A", source: new S104CoverageSource(
            new S104Dataset { HorizontalCRS = 4326, DataCodingFormat = 2, VerticalDatum = Lat, Coverages = water.Coverages })));
        return catalog;
    }

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
    public async Task A_polyline_and_draft_check_the_passage()
    {
        var value = await Ok(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight, IncludeSamples: true));

        Assert.Equal("meets_settings", value.Clearance!.Verdict);
        Assert.Equal("Meets your settings", value.Clearance!.VerdictTitle);
        Assert.False(value.Clearance!.NoMarginSet);
        Assert.Null(value.RouteId);
        var min = Assert.IsType<CheckRouteMinimum>(value.Clearance!.Minimum);
        Assert.InRange(min.UkcMeters, 4.0, 4.05);
        Assert.Equal("meets_settings", min.State);
        var leg = Assert.Single(value.Clearance!.Legs!);
        Assert.Equal("Leg 1 → 2", leg.Name);
        Assert.InRange(leg.DistanceNm, 1.37, 1.39);
        Assert.Equal(Midnight, leg.Start);
        Assert.Empty(value.Clearance!.Gaps);
        Assert.Empty(value.Clearance!.LowConfidence);
        Assert.Empty(value.Clearance!.Confidence);
        Assert.Empty(leg.Confidence);
        Assert.Equal(["102A"], value.Clearance!.Sources.BathymetryDatasets);
        Assert.Equal("104A", Assert.Single(value.Clearance!.Sources.WaterLevels).Dataset);
        Assert.Equal("Lowest Astronomical Tide", Assert.Single(value.Clearance!.Sources.DepthDatums).Label);
        Assert.Equal("computed from S-102, S-104 for your settings", value.Clearance!.Disclaimer);
        Assert.EndsWith(" · clearance computed from S-102, S-104 for your settings", value.Disclaimer, StringComparison.Ordinal);
        Assert.NotNull(value.Clearance!.Samples);
        Assert.Null(value.Clearance!.Window);

        // Every check ran; without safety settings the chart check's depth
        // checks didn't, and with no cells the route leaves chart coverage.
        Assert.Equal(["chart", "areas", "warnings", "clearance"], value.Checks.Select(c => c.Name));
        Assert.Equal(["partial", "ran", "ran", "ran"], value.Checks.Select(c => c.State));
        Assert.Equal(RouteCheckReasons.SafetySettingsMissing, value.Checks[0].Reason);
        Assert.All(value.Findings, f => Assert.Equal(RouteFindingCodes.OutsideChartCoverage.Value, f.Code));
        Assert.Equal("check", value.Verdict.Kind);
        Assert.Equal(value.Findings[0].Id, value.Verdict.HeadlineId);
        Assert.Equal(["102A", "104A"], value.Datasets);
    }

    [Fact]
    public async Task An_undeclared_s102_datum_keeps_the_band_and_reports_confidence_separately()
    {
        // #939: the S-102 declares no vertical datum, so every sample is
        // datum_unconfirmed, but still meets the setting.
        var value = await Ok(new CheckRouteTool(Catalog(depthDatum: null)), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight,
            IncludeSamples: true, IncludeWindow: true, WindowFrom: Midnight, WindowTo: Midnight.AddMinutes(30)));

        Assert.Equal("meets_settings", value.Clearance!.Verdict);
        Assert.Equal(["datum_unconfirmed"], value.Clearance!.Confidence);
        Assert.Equal("meets_settings", value.Clearance!.Minimum!.State);
        Assert.Equal(["datum_unconfirmed"], value.Clearance!.Minimum.Confidence);
        var leg = Assert.Single(value.Clearance!.Legs!);
        Assert.Equal("meets_settings", leg.WorstState);
        Assert.True(leg.LowConfidence);
        Assert.Equal(["datum_unconfirmed"], leg.Confidence);
        Assert.All(value.Clearance!.Samples!, s =>
        {
            Assert.Equal("meets_settings", s.State);
            Assert.Equal(["datum_unconfirmed"], s.Confidence);
        });
        Assert.Equal("datum_unconfirmed", Assert.Single(value.Clearance!.LowConfidence).Kind);

        var window = Assert.IsType<CheckRouteWindow>(value.Clearance!.Window);
        Assert.NotEmpty(window.Windows);
        Assert.Equal(["datum_unconfirmed"], window.WindowConfidence);
        Assert.All(window.Departures, d => Assert.Equal(["datum_unconfirmed"], d.Confidence));
    }

    [Fact]
    public async Task Below_the_setting_is_counted_and_located()
    {
        // Column 2 (0,02–0,03° E) is 9 m deep.
        var value = await Ok(new CheckRouteTool(Catalog((r, c) => c == 2 ? 9f : 12.5f)), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 1, Departure: Midnight, IncludeSamples: true));

        Assert.Equal("below_setting", value.Clearance!.Verdict);
        Assert.Equal("Below your UKC setting in 1 place", value.Clearance!.VerdictTitle);
        Assert.Equal("must_review", value.Verdict.Kind);
        var below = Assert.Single(value.Findings, f => f.Code == RouteFindingCodes.BelowSetting.Value);
        Assert.Equal(below.Id, value.Verdict.HeadlineId);
        Assert.Equal("clearance", below.Category);
        Assert.NotNull(below.Details?.Clearance);
        Assert.Equal(1, value.Clearance!.PlacesBelow);
        Assert.InRange(value.Clearance!.MinutesBelow, 4.3, 5.5); // 0,02–0,028° E at 6 kn
        Assert.InRange(value.Clearance!.Minimum!.Longitude, 0.02, 0.03);
        Assert.Equal("below_setting", value.Clearance!.Legs![0].WorstState);
        var sample = value.Clearance!.Samples!.First(s => s.State == "below_setting");
        Assert.Equal("s102", sample.DepthSource);
        Assert.Equal("102A", sample.DepthDataset);
        Assert.Equal("applied", sample.WaterLevelStatus);
        Assert.Equal("same", sample.Datum);
        Assert.NotNull(sample.TimeStep);
    }

    [Fact]
    public async Task Unset_required_ukc_says_no_margin_is_set()
    {
        var value = await Ok(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, Departure: Midnight, IncludeSamples: true));

        Assert.True(value.Clearance!.NoMarginSet);
        Assert.Equal("Meets your settings · no margin set", value.Clearance!.VerdictTitle);
        Assert.Null(value.Clearance!.Legs![0].RequiredUkcMeters);
        Assert.Equal(0, value.Clearance!.Minimum!.RequiredUkcMeters);
    }

    [Fact]
    public async Task Arrival_past_the_s104_end_gives_the_neutral_verdict()
    {
        var value = await Ok(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, Departure: Midnight.AddMinutes(110), IncludeSamples: true));

        Assert.Equal("water_level_ends_before_arrival", value.Clearance!.Verdict);
        Assert.Equal("Arrival is after the water-level data ends · no margin set", value.Clearance!.VerdictTitle);
        Assert.Equal("water_level_after_end", Assert.Single(value.Clearance!.Gaps).Kind);
        Assert.True(value.Clearance!.Legs![0].NoWaterLevel);
        Assert.Equal("water_level_ends_before_arrival", value.Verdict.Kind);
        Assert.Contains(value.Findings, f => f.Code == RouteFindingCodes.WaterLevelEnds.Value && f.Id == value.Verdict.HeadlineId);
    }

    [Fact]
    public async Task Samples_are_thinned_but_keep_the_minimum()
    {
        var value = await Ok(new CheckRouteTool(Catalog((r, c) => c == 2 ? 9f : 12.5f)), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, Departure: Midnight, IncludeSamples: true, MaxSamples: 5));

        Assert.True(value.Clearance!.SamplesThinned);
        Assert.Equal(5, value.Clearance!.Samples!.Count);
        Assert.Contains(value.Clearance!.Samples, s => s.Index == value.Clearance!.Minimum!.Sample);
        Assert.True(value.Clearance!.SampleCount > 5);
    }

    [Fact]
    public async Task The_window_sweeps_departures()
    {
        var value = await Ok(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight,
            IncludeWindow: true, WindowFrom: Midnight, WindowTo: Midnight.AddHours(2), WindowStepMinutes: 30));

        var window = Assert.IsType<CheckRouteWindow>(value.Clearance!.Window);
        Assert.Equal(5, window.Departures.Count);

        // The passage takes about 14 minutes and the data ends at 02:00.
        Assert.Equal(Midnight.AddHours(1.5), Assert.Single(window.Windows).Last);
        Assert.Equal("water_level_ends_before_arrival", window.Departures[^1].Verdict);
        Assert.Equal(Midnight, window.NearestMeetingDeparture);
    }

    [Fact]
    public async Task Include_window_sweeps_the_s104_range_and_reports_the_late_arrivals()
    {
        var value = await Ok(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight, IncludeWindow: true));

        var window = Assert.IsType<CheckRouteWindow>(value.Clearance!.Window);
        Assert.Equal(5, window.StepMinutes);
        Assert.Equal(Midnight.AddHours(2), window.To);
        Assert.Equal((int)((window.To - window.From).TotalMinutes / 5) + 1, window.Departures.Count);

        // The passage takes about 14 minutes and the data ends at 02:00.
        var late = Assert.IsType<CheckRouteWindowRange>(window.ArrivalAfterDataEnds);
        Assert.Equal(window.To, late.Last);
        Assert.InRange(late.First, Midnight.AddMinutes(100), Midnight.AddMinutes(110));
        Assert.All(window.Departures.Where(d => d.Departure >= late.First), d => Assert.Equal("water_level_ends_before_arrival", d.Verdict));
        Assert.True(window.Windows[^1].Last < late.First);
    }

    [Fact]
    public async Task Without_a_draft_the_clearance_check_is_skipped()
    {
        var value = await Ok(new CheckRouteTool(Catalog()), new CheckRouteRequest(Polyline: Polyline, IncludeWindow: true));

        Assert.Null(value.Clearance);
        var clearance = value.Checks.Single(c => c.Name == "clearance");
        Assert.Equal("skipped", clearance.State);
        Assert.Equal(RouteCheckReasons.DraftMissing, clearance.Reason);
        Assert.DoesNotContain("clearance", value.Disclaimer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1.0, null, null)]
    [InlineData(10.0, 0.0, null)]
    [InlineData(10.0, null, -0.5)]
    public async Task Invalid_values_are_rejected(double draft, double? speed, double? required)
    {
        var error = await Err(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: draft, SpeedKnots: speed, RequiredUkcMeters: required));

        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Window_bounds_need_include_window()
    {
        var error = await Err(new CheckRouteTool(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, DraftMeters: 10, WindowFrom: Midnight, WindowTo: Midnight.AddHours(1)));

        Assert.Equal("includeWindow", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task Without_routes_a_polyline_is_required()
    {
        var error = await Err(new CheckRouteTool(Catalog()), new CheckRouteRequest(DraftMeters: 10));

        Assert.Equal("polyline", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task A_host_route_uses_its_leg_values_and_departure_with_request_overrides()
    {
        var routes = new FakeRoutes(new PassageRouteSnapshot(
            "r1",
            "Test route",
            [
                new PassageRouteLeg([new GeoPosition(0.005, 0.005), new GeoPosition(0.005, 0.02)], LegGeometry.Rhumb, 10, 6, 0.2, 2, "Leg 1 → 2"),
                new PassageRouteLeg([new GeoPosition(0.005, 0.02), new GeoPosition(0.005, 0.028)], LegGeometry.Rhumb, 10, 12, null, null, "Leg 2 → 3"),
            ],
            Midnight.AddMinutes(30)));

        var value = await Ok(new CheckRouteTool(Catalog(), routes), new CheckRouteRequest(RequiredUkcMeters: 1, IncludeSamples: true));

        Assert.Null(routes.LastRouteId);
        Assert.Equal("r1", value.RouteId);
        Assert.Equal("Test route", value.RouteName);
        Assert.Equal(Midnight.AddMinutes(30), value.Clearance!.Departure);
        Assert.Equal(2, value.Clearance!.Legs!.Count);
        Assert.Equal(0.2, value.Clearance!.Legs![0].SquatMeters);
        Assert.Equal(0, value.Clearance!.Legs![1].SquatMeters);
        Assert.Equal(12, value.Clearance!.Legs![1].SpeedKnots);
        Assert.All(value.Clearance!.Legs!, l => Assert.Equal(1, l.RequiredUkcMeters));
        Assert.False(value.Clearance!.NoMarginSet);

        var overridden = await Ok(new CheckRouteTool(Catalog(), routes), new CheckRouteRequest(
            RouteId: "r1", DraftMeters: 12, SpeedKnots: 3, Departure: Midnight, IncludeSamples: true));
        Assert.Equal("r1", routes.LastRouteId);
        Assert.All(overridden.Clearance!.Legs!, l =>
        {
            Assert.Equal(12, l.DraftMeters);
            Assert.Equal(3, l.SpeedKnots);
        });
        Assert.Equal(Midnight, overridden.Clearance!.Departure);
    }

    [Fact]
    public async Task A_route_leg_without_a_draft_takes_one_from_the_request()
    {
        var routes = new FakeRoutes(new PassageRouteSnapshot(
            "r1", null,
            [new PassageRouteLeg([new GeoPosition(0.005, 0.005), new GeoPosition(0.005, 0.02)], LegGeometry.Rhumb, null, 6, null, null, null)],
            Midnight));

        var skipped = await Ok(new CheckRouteTool(Catalog(), routes), new CheckRouteRequest());
        Assert.Null(skipped.Clearance);
        Assert.Equal(RouteCheckReasons.DraftMissing, skipped.Checks.Single(c => c.Name == "clearance").Reason);

        var value = await Ok(new CheckRouteTool(Catalog(), routes), new CheckRouteRequest(DraftMeters: 10, IncludeSamples: true));
        Assert.Equal(10, value.Clearance!.Legs![0].DraftMeters);
    }

    [Fact]
    public async Task Route_lookup_errors_pass_through()
    {
        var error = await Err(new CheckRouteTool(Catalog(), new FakeRoutes(null)), new CheckRouteRequest(RouteId: "nope"));

        Assert.Equal("invalid_argument", error.Code);
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
