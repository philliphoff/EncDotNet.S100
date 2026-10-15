using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>get_passage_result</c> on synthetic S-102 + S-104 grids: a 4 × 4 grid
/// of 0,01° cells from (0, 0), 12,5 m deep, with water levels of 1,5 / 1,6 /
/// 1,7 m at 00:00 / 01:00 / 02:00 on 1 January 2024, both on LAT.
/// </summary>
public class GetPassageResultToolTests
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

    private static async Task<GetPassageResultResult> Ok(GetPassageResultTool tool, GetPassageResultRequest request)
    {
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static async Task<ToolError> Err(GetPassageResultTool tool, GetPassageResultRequest request)
    {
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetError(out var error));
        return error!;
    }

    [Fact]
    public async Task A_polyline_and_draft_check_the_passage()
    {
        var value = await Ok(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight));

        Assert.Equal("meets_settings", value.Verdict);
        Assert.Equal("Meets your settings", value.VerdictTitle);
        Assert.False(value.NoMarginSet);
        Assert.Null(value.RouteId);
        var min = Assert.IsType<PassageMinimumSummary>(value.Minimum);
        Assert.InRange(min.UkcMeters, 4.0, 4.05);
        Assert.Equal("meets_settings", min.State);
        var leg = Assert.Single(value.Legs);
        Assert.Equal("Leg 1 → 2", leg.Name);
        Assert.InRange(leg.DistanceNm, 1.37, 1.39);
        Assert.Equal(Midnight, leg.Start);
        Assert.Empty(value.Gaps);
        Assert.Empty(value.LowConfidence);
        Assert.Empty(value.Confidence);
        Assert.Empty(leg.Confidence);
        Assert.Equal(["102A"], value.Sources.BathymetryDatasets);
        Assert.Equal("104A", Assert.Single(value.Sources.WaterLevels).Dataset);
        Assert.Equal("Lowest Astronomical Tide", Assert.Single(value.Sources.DepthDatums).Label);
        Assert.Equal("Not for navigation · computed from S-102, S-104 for your settings", value.Disclaimer);
        Assert.Null(value.Samples);
        Assert.Null(value.Window);
    }

    [Fact]
    public async Task An_undeclared_s102_datum_keeps_the_band_and_reports_confidence_separately()
    {
        // #939: the S-102 declares no vertical datum, so every sample is
        // datum_unconfirmed, but still meets the setting.
        var value = await Ok(new GetPassageResultTool(Catalog(depthDatum: null)), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight,
            IncludeSamples: true, WindowFrom: Midnight, WindowTo: Midnight.AddMinutes(30)));

        Assert.Equal("meets_settings", value.Verdict);
        Assert.Equal(["datum_unconfirmed"], value.Confidence);
        Assert.Equal("meets_settings", value.Minimum!.State);
        Assert.Equal(["datum_unconfirmed"], value.Minimum.Confidence);
        var leg = Assert.Single(value.Legs);
        Assert.Equal("meets_settings", leg.WorstState);
        Assert.True(leg.LowConfidence);
        Assert.Equal(["datum_unconfirmed"], leg.Confidence);
        Assert.All(value.Samples!, s =>
        {
            Assert.Equal("meets_settings", s.State);
            Assert.Equal(["datum_unconfirmed"], s.Confidence);
        });
        Assert.Equal("datum_unconfirmed", Assert.Single(value.LowConfidence).Kind);

        var window = Assert.IsType<PassageWindowSummary>(value.Window);
        Assert.NotEmpty(window.Windows);
        Assert.Equal(["datum_unconfirmed"], window.WindowConfidence);
        Assert.All(window.Departures, d => Assert.Equal(["datum_unconfirmed"], d.Confidence));
    }

    [Fact]
    public async Task Below_the_setting_is_counted_and_located()
    {
        // Column 2 (0,02–0,03° E) is 9 m deep.
        var value = await Ok(new GetPassageResultTool(Catalog((r, c) => c == 2 ? 9f : 12.5f)), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 1, Departure: Midnight, IncludeSamples: true));

        Assert.Equal("below_setting", value.Verdict);
        Assert.Equal("Below your UKC setting in 1 place", value.VerdictTitle);
        Assert.Equal(1, value.PlacesBelow);
        Assert.InRange(value.MinutesBelow, 4.3, 5.5); // 0,02–0,028° E at 6 kn
        Assert.InRange(value.Minimum!.Longitude, 0.02, 0.03);
        Assert.Equal("below_setting", value.Legs[0].WorstState);
        var sample = value.Samples!.First(s => s.State == "below_setting");
        Assert.Equal("s102", sample.DepthSource);
        Assert.Equal("102A", sample.DepthDataset);
        Assert.Equal("applied", sample.WaterLevelStatus);
        Assert.Equal("same", sample.Datum);
        Assert.NotNull(sample.TimeStep);
    }

    [Fact]
    public async Task Unset_required_ukc_says_no_margin_is_set()
    {
        var value = await Ok(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, Departure: Midnight));

        Assert.True(value.NoMarginSet);
        Assert.Equal("Meets your settings · no margin set", value.VerdictTitle);
        Assert.Null(value.Legs[0].RequiredUkcMeters);
        Assert.Equal(0, value.Minimum!.RequiredUkcMeters);
    }

    [Fact]
    public async Task Arrival_past_the_s104_end_gives_the_neutral_verdict()
    {
        var value = await Ok(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, Departure: Midnight.AddMinutes(110)));

        Assert.Equal("water_level_ends_before_arrival", value.Verdict);
        Assert.Equal("Arrival is after the water-level data ends · no margin set", value.VerdictTitle);
        Assert.Equal("water_level_after_end", Assert.Single(value.Gaps).Kind);
        Assert.True(value.Legs[0].NoWaterLevel);
    }

    [Fact]
    public async Task Samples_are_thinned_but_keep_the_minimum()
    {
        var value = await Ok(new GetPassageResultTool(Catalog((r, c) => c == 2 ? 9f : 12.5f)), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, Departure: Midnight, IncludeSamples: true, MaxSamples: 5));

        Assert.True(value.SamplesThinned);
        Assert.Equal(5, value.Samples!.Count);
        Assert.Contains(value.Samples, s => s.Index == value.Minimum!.Sample);
        Assert.True(value.SampleCount > 5);
    }

    [Fact]
    public async Task The_window_sweeps_departures()
    {
        var value = await Ok(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight,
            WindowFrom: Midnight, WindowTo: Midnight.AddHours(2), WindowStepMinutes: 30));

        var window = Assert.IsType<PassageWindowSummary>(value.Window);
        Assert.Equal(5, window.Departures.Count);

        // The passage takes about 14 minutes and the data ends at 02:00.
        Assert.Equal(Midnight.AddHours(1.5), Assert.Single(window.Windows).Last);
        Assert.Equal("water_level_ends_before_arrival", window.Departures[^1].Verdict);
        Assert.Equal(Midnight, window.NearestMeetingDeparture);
    }

    [Fact]
    public async Task Include_window_sweeps_the_s104_range_and_reports_the_late_arrivals()
    {
        var value = await Ok(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: 10, SpeedKnots: 6, RequiredUkcMeters: 3, Departure: Midnight, IncludeWindow: true));

        var window = Assert.IsType<PassageWindowSummary>(value.Window);
        Assert.Equal(5, window.StepMinutes);
        Assert.Equal(Midnight.AddHours(2), window.To);
        Assert.Equal((int)((window.To - window.From).TotalMinutes / 5) + 1, window.Departures.Count);

        // The passage takes about 14 minutes and the data ends at 02:00.
        var late = Assert.IsType<PassageWindowRangeSummary>(window.ArrivalAfterDataEnds);
        Assert.Equal(window.To, late.Last);
        Assert.InRange(late.First, Midnight.AddMinutes(100), Midnight.AddMinutes(110));
        Assert.All(window.Departures.Where(d => d.Departure >= late.First), d => Assert.Equal("water_level_ends_before_arrival", d.Verdict));
        Assert.True(window.Windows[^1].Last < late.First);
    }

    [Fact]
    public async Task A_polyline_needs_a_draft()
    {
        var error = await Err(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(Polyline: Polyline));

        Assert.Equal("draftMeters", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Theory]
    [InlineData(-1.0, null, null)]
    [InlineData(10.0, 0.0, null)]
    [InlineData(10.0, null, -0.5)]
    public async Task Invalid_values_are_rejected(double draft, double? speed, double? required)
    {
        var error = await Err(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(
            Polyline: Polyline, DraftMeters: draft, SpeedKnots: speed, RequiredUkcMeters: required));

        Assert.IsType<InvalidArgument>(error);
    }

    [Fact]
    public async Task Without_routes_a_polyline_is_required()
    {
        var error = await Err(new GetPassageResultTool(Catalog()), new GetPassageResultRequest(DraftMeters: 10));

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

        var value = await Ok(new GetPassageResultTool(Catalog(), routes), new GetPassageResultRequest(RequiredUkcMeters: 1));

        Assert.Null(routes.LastRouteId);
        Assert.Equal("r1", value.RouteId);
        Assert.Equal("Test route", value.RouteName);
        Assert.Equal(Midnight.AddMinutes(30), value.Departure);
        Assert.Equal(2, value.Legs.Count);
        Assert.Equal(0.2, value.Legs[0].SquatMeters);
        Assert.Equal(0, value.Legs[1].SquatMeters);
        Assert.Equal(12, value.Legs[1].SpeedKnots);
        Assert.All(value.Legs, l => Assert.Equal(1, l.RequiredUkcMeters));
        Assert.False(value.NoMarginSet);

        var overridden = await Ok(new GetPassageResultTool(Catalog(), routes), new GetPassageResultRequest(
            RouteId: "r1", DraftMeters: 12, SpeedKnots: 3, Departure: Midnight));
        Assert.Equal("r1", routes.LastRouteId);
        Assert.All(overridden.Legs, l =>
        {
            Assert.Equal(12, l.DraftMeters);
            Assert.Equal(3, l.SpeedKnots);
        });
        Assert.Equal(Midnight, overridden.Departure);
    }

    [Fact]
    public async Task A_route_leg_without_a_draft_needs_one_from_the_request()
    {
        var routes = new FakeRoutes(new PassageRouteSnapshot(
            "r1", null,
            [new PassageRouteLeg([new GeoPosition(0.005, 0.005), new GeoPosition(0.005, 0.02)], LegGeometry.Rhumb, null, 6, null, null, null)],
            Midnight));

        var error = await Err(new GetPassageResultTool(Catalog(), routes), new GetPassageResultRequest());
        Assert.Equal("draftMeters", Assert.IsType<InvalidArgument>(error).Parameter);

        var value = await Ok(new GetPassageResultTool(Catalog(), routes), new GetPassageResultRequest(DraftMeters: 10));
        Assert.Equal(10, value.Legs[0].DraftMeters);
    }

    [Fact]
    public async Task Route_lookup_errors_pass_through()
    {
        var error = await Err(new GetPassageResultTool(Catalog(), new FakeRoutes(null)), new GetPassageResultRequest(RouteId: "nope"));

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
