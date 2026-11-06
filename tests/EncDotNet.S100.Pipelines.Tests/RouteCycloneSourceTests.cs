using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1299: loaded NHC advisories as the weather check's tropical
/// cyclones, and the check run over one. Fixture: Hurricane Ian, advisory 20
/// (synoptic 00:00 UTC 28 Sep 2022), with its wind radii and best track.
/// </summary>
public sealed class RouteCycloneSourceTests
{
    private static readonly DateTimeOffset Synoptic = new(2022, 9, 28, 0, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LoadedDataset Ian(string id = "ian") =>
        LoadedDatasetProjector.Project(new DatasetId(id), new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast))
        ?? throw new InvalidOperationException("Ian didn't load.");

    private static RouteCheckSources Sources(params LoadedDataset[] datasets) =>
        CatalogRouteCheckSources.FromDatasets(datasets, new ProjNetCrsTransformFactory());

    [Fact]
    public void Without_advisories_the_sources_have_no_cyclones()
    {
        Assert.Empty(Sources().Cyclones!);
    }

    [Fact]
    public void An_advisory_gives_the_storm_at_a_time_and_its_cone()
    {
        var data = (TropicalCycloneDatasetData)Ian().Data;
        var cyclone = Assert.Single(Sources(Ian()).Cyclones!);

        Assert.Equal(new RouteCycloneInfo("ian", "AL092022", "Ian", "20", data.Advisory.IssueTime), cyclone.Info);
        Assert.Equal(Synoptic, cyclone.ForecastStart);
        Assert.Equal(Synoptic.AddHours(96), cyclone.ForecastEnd);
        Assert.True(cyclone.Track.Count > data.Advisory.Forecast.Count);

        // Between the first two points: estimated, with all three radii.
        var at = cyclone.At(Synoptic.AddHours(6))!;
        var expected = data.Advisory.StormAt(Synoptic.UtcDateTime.AddHours(6))!;
        Assert.Equal(RouteCycloneBasis.Estimated, at.Basis);
        Assert.Equal(expected.Point.Position, at.Centre);
        Assert.Equal([TropicalCycloneRadius.Knots34, TropicalCycloneRadius.Knots50, TropicalCycloneRadius.Knots64], at.Radii.Select(r => r.Radius).Order());
        Assert.Equal(expected.Point.Radii.Single(r => r.ThresholdKnots == 64).NortheastNauticalMiles, at.RadiiOf(TropicalCycloneRadius.Knots64)!.NortheastNauticalMiles);
        Assert.Equal(RouteCycloneBasis.BestTrack, cyclone.At(Synoptic.AddHours(-12))!.Basis);
        Assert.Null(cyclone.At(Synoptic.AddHours(97)));

        Assert.True(cyclone.InCone(data.Advisory.Forecast[2].Position));
        Assert.False(cyclone.InCone(new GeoPosition(10, -40)));
    }

    [Fact]
    public void Only_the_last_advisory_of_a_storm_is_checked()
    {
        var older = Ian("ian-19") with { };
        var data = (TropicalCycloneDatasetData)older.Data;
        older = older with { Data = data with { Advisory = data.Advisory with { Advisory = "19", IssueTime = data.Advisory.IssueTime.AddHours(-6) } } };

        var cyclone = Assert.Single(Sources(older, Ian()).Cyclones!);

        Assert.Equal("ian", cyclone.Info.DatasetId);
    }

    [Fact]
    public void A_route_through_the_storm_six_hours_after_the_advisory_meets_every_radius_and_the_cone()
    {
        var data = (TropicalCycloneDatasetData)Ian().Data;
        var centre = data.Advisory.StormAt(Synoptic.UtcDateTime.AddHours(6))!.Point.Position;
        var west = new GeoPosition(centre.Latitude, centre.Longitude - 1.5);
        var east = new GeoPosition(centre.Latitude, centre.Longitude + 1.5);
        var half = Wgs84Geodesy.RhumbDistanceMetres(west, centre) / PassageGeodesy.MetersPerNauticalMile;
        var departure = Synoptic.AddHours(6) - TimeSpan.FromHours(half / 10);
        var plan = new PassagePlan(
            [new PassageLeg { Points = [west, centre], Geometry = LegGeometry.Rhumb, Speed = Speed.FromKnots(10), Name = "Leg 1 → 2" },
             new PassageLeg { Points = [centre, east], Geometry = LegGeometry.Rhumb, Speed = Speed.FromKnots(10), Name = "Leg 2 → 3" }],
            departure);

        var report = new RouteChecker(Sources(Ian())).Check(plan, new RouteCheckSettings { At = departure }, Ct);

        Assert.Equal(RouteCheckState.Ran, report.Status(WeatherCheck.CheckName)!.State);
        Assert.Equal(WeatherVerdict.InsideCycloneRadius, report.Weather!.Verdict);
        var weather = report.Findings.Where(f => f.Category == RouteFindingCategory.Weather).ToList();
        Assert.Equal(RouteFindingGroup.MustReview, weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius64).Group);
        Assert.Equal(RouteFindingGroup.MustReview, weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius50).Group);
        Assert.Equal(RouteFindingGroup.Check, weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius34).Group);
        Assert.Contains(weather, f => f.Code == RouteFindingCodes.CycloneCone);
        Assert.EndsWith("Ian adv 20", weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius64).Meta, StringComparison.Ordinal);

        var cpa = Assert.IsType<TropicalCycloneDetails>(weather.Single(f => f.Code == RouteFindingCodes.CycloneClosestApproach).Details);
        Assert.True(cpa.ClosestDistance!.Value.TotalMetres < 2 * PassageGeodesy.MetersPerNauticalMile);
        Assert.InRange((cpa.StartTime - Synoptic).TotalHours, 5.8, 6.2);
        Assert.Equal(RouteCycloneBasis.Estimated, cpa.StormAtClosest!.Basis);
    }
}
