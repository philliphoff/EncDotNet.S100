using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>check_route</c>'s tropical cyclone rule (#1299) over Hurricane Ian,
/// advisory 20 (synoptic 00:00 UTC 28 Sep 2022): a polyline due east through
/// the storm's estimated centre six hours later, run at 10 kn so the vessel
/// is at the centre then.
/// </summary>
public class CheckRouteCycloneTests
{
    private static readonly DateTimeOffset Synoptic = new(2022, 9, 28, 0, 0, 0, TimeSpan.Zero);

    private static FakeDatasetCatalog Catalog()
    {
        var catalog = new FakeDatasetCatalog();
        using var stream = File.OpenRead(TropicalCycloneFixtures.IanForecast);
        catalog.Add(LoadedDatasetProjector.Project(new DatasetId("ian"), "TC", stream)!);
        return catalog;
    }

    private static (GeoPolyline Polyline, DateTimeOffset Departure) Route()
    {
        var data = (TropicalCycloneDatasetData)Catalog().Datasets.Single().Data;
        var centre = data.Advisory.StormAt(Synoptic.UtcDateTime.AddHours(6))!.Point.Position;
        var west = new GeoPosition(centre.Latitude, centre.Longitude - 1.5);
        var half = Wgs84Geodesy.RhumbDistanceMetres(west, centre) / PassageGeodesy.MetersPerNauticalMile;
        return (
            new GeoPolyline([new GeoPoint(west.Latitude, west.Longitude), new GeoPoint(centre.Latitude, centre.Longitude), new GeoPoint(centre.Latitude, centre.Longitude + 1.5)]),
            Synoptic.AddHours(6) - TimeSpan.FromHours(half / 10));
    }

    private static async Task<CheckRouteResult> Ok(CheckRouteRequest request)
    {
        var result = await new CheckRouteTool(Catalog()).InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    [Fact]
    public async Task At_the_defaults_every_radius_the_cone_and_the_closest_approach_are_reported()
    {
        var (polyline, departure) = Route();

        var value = await Ok(new CheckRouteRequest(Polyline: polyline, SpeedKnots: 10, Departure: departure));

        var check = value.Checks.Single(c => c.Name == "weather");
        Assert.Equal("ran", check.State);
        var weather = value.Weather!;
        Assert.Equal("inside_cyclone_radius", weather.Verdict);
        Assert.Empty(weather.Limits);
        Assert.Equal(new CheckRouteCycloneSettings(34, true, 50), weather.CycloneSettings);
        Assert.Contains(CheckRouteWeather.CycloneDisclaimerText, value.Disclaimer, StringComparison.Ordinal);

        var storm = Assert.Single(weather.Cyclones);
        Assert.Equal(("ian", "AL092022", "Ian", "20"), (storm.DatasetId, storm.StormId, storm.Name, storm.Advisory));
        Assert.Equal(64, storm.StrongestRadiusKnots);
        Assert.True(storm.InCone);
        Assert.Equal("estimated", storm.ClosestApproach!.Basis);
        Assert.True(storm.ClosestApproach.DistanceNm < 2);
        Assert.InRange((storm.ClosestApproach.Eta!.Value - Synoptic).TotalHours, 5.8, 6.2);

        Assert.Equal("must_review", value.Findings.Single(f => f.Code == "weather.tc_radius_64").Group);
        Assert.Equal("must_review", value.Findings.Single(f => f.Code == "weather.tc_radius_50").Group);
        Assert.Equal("check", value.Findings.Single(f => f.Code == "weather.tc_radius_34").Group);
        Assert.Equal("check", value.Findings.Single(f => f.Code == "weather.tc_cone").Group);
        var cpa = value.Findings.Single(f => f.Code == "weather.tc_cpa");
        Assert.Equal("info", cpa.Group);
        Assert.Equal("closest_approach", cpa.Details!.Cyclone!.Kind);
        Assert.Equal("estimated", cpa.Details.Cyclone.Basis);
        var r64 = value.Findings.Single(f => f.Code == "weather.tc_radius_64").Details!.Cyclone!;
        Assert.Equal(("radius", 64, "AL092022"), (r64.Kind, r64.RadiusKnots, r64.StormId));
        Assert.NotNull(r64.EndTime);
    }

    [Fact]
    public async Task The_settings_change_the_radii_tested_their_groups_and_the_closest_approach()
    {
        var (polyline, departure) = Route();

        var value = await Ok(new CheckRouteRequest(
            Polyline: polyline, SpeedKnots: 10, Departure: departure,
            CycloneCheckAgainstKnots: 50, CycloneReportClosestApproach: false, CycloneMustReviewInside: "never"));

        Assert.Equal(new CheckRouteCycloneSettings(50, false, null), value.Weather!.CycloneSettings);
        var codes = value.Findings.Where(f => f.Category == "weather").Select(f => (f.Code, f.Group)).OrderBy(c => c.Code, StringComparer.Ordinal);
        Assert.Equal([("weather.tc_cone", "check"), ("weather.tc_radius_50", "check"), ("weather.tc_radius_64", "check")], codes);
    }

    [Fact]
    public async Task A_departure_window_sweeps_the_storm()
    {
        var (polyline, departure) = Route();

        var value = await Ok(new CheckRouteRequest(
            Polyline: polyline, SpeedKnots: 10, Departure: departure,
            IncludeWindow: true, WindowFrom: departure, WindowTo: departure.AddHours(2), WindowStepMinutes: 60));

        var sweep = value.Weather!.Window!;
        Assert.Equal(3, sweep.Departures.Count);
        Assert.Equal(64, sweep.Departures[0].CycloneRadiusKnots);
        Assert.Equal("inside_cyclone_radius", sweep.Departures[0].Verdict);
        Assert.NotNull(sweep.Departures[0].CycloneClosestNm);
    }

    [Theory]
    [InlineData(40, null, "cycloneCheckAgainstKnots")]
    [InlineData(null, "gale", "cycloneMustReviewInside")]
    [InlineData(null, "40", "cycloneMustReviewInside")]
    public async Task Bad_cyclone_settings_are_refused(int? against, string? inside, string parameter)
    {
        var (polyline, _) = Route();
        var result = await new CheckRouteTool(Catalog()).InvokeAsync(
            new CheckRouteRequest(Polyline: polyline, CycloneCheckAgainstKnots: against, CycloneMustReviewInside: inside),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Contains(parameter, error!.Message, StringComparison.Ordinal);
    }
}
