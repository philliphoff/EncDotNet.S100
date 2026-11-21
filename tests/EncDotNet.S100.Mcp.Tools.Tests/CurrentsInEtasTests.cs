using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// S-111 currents in every check's ETAs (#1033), over a synthetic S-111 grid:
/// 0.5 kn setting east over 0–0.04°, hourly from 2024-01-01 00:00 to 02:00 UTC.
/// </summary>
public sealed class CurrentsInEtasTests
{
    private static readonly DateTimeOffset InRange = new(2024, 1, 1, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public void S111_answers_where_it_covers_and_the_fallback_elsewhere()
    {
        var fallback = new Steady("P1", 1.5, 270);
        var source = new CatalogCurrentSource([LoadedDatasetFactory.S111("s111")], new ProjNetCrsTransformFactory(), fallback);

        var inside = source.Locate(new GeoPosition(0.02, 0.02))!.Read(InRange)!;
        Assert.Equal(RouteCurrentOrigin.S111, inside.Source.Origin);
        Assert.Equal("s111", inside.Source.Id);
        Assert.Equal(0.5, inside.Current.SpeedKnots, 2);
        Assert.Equal(90, inside.Current.SetDegrees, 1);

        // Past the forecast, and outside its bounds, the prediction answers.
        var late = source.Locate(new GeoPosition(0.02, 0.02))!.Read(InRange.AddDays(1))!;
        Assert.Equal("P1", late.Source.Id);
        Assert.Equal("P1", source.Locate(new GeoPosition(1, 1))!.Source.Id);
    }

    [Fact]
    public void The_route_check_sources_use_S111_when_it_is_loaded()
    {
        var sources = CatalogRouteCheckSources.FromDatasets([LoadedDatasetFactory.S111("s111")], new ProjNetCrsTransformFactory());

        Assert.IsType<CatalogCurrentSource>(sources.Currents);
        Assert.Null(CatalogRouteCheckSources.FromDatasets([], new ProjNetCrsTransformFactory()).Currents);
    }

    [Fact]
    public async Task Check_route_with_speeds_through_the_water_applies_S111_to_the_ETAs()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111("s111"));
        var tool = new CheckRouteTool(catalog, null, new RouteCheckSourceCache(new ProjNetCrsTransformFactory()), TimeProvider.System);
        var line = new GeoPolyline([new GeoPoint(0.02, 0.0), new GeoPoint(0.02, 0.03)]);

        var over = await tool.InvokeAsync(new CheckRouteRequest(Polyline: line, Departure: InRange, Vessel: new VesselArguments(SpeedKnots: 2)), TestContext.Current.CancellationToken);
        var through = await tool.InvokeAsync(new CheckRouteRequest(Polyline: line, Departure: InRange, Vessel: new VesselArguments(SpeedKnots: 2, SpeedThroughWater: true)), TestContext.Current.CancellationToken);

        Assert.True(over.TryGetValue(out var o));
        Assert.Null(o!.Currents);
        Assert.True(through.TryGetValue(out var t));
        var currents = t!.Currents!;
        Assert.True(currents.Applied);
        Assert.Equal([("s111", "s111")], currents.Sources.Select(s => (s.Id, s.Origin)));
        Assert.Equal(0, currents.WithoutCurrentNm);
        Assert.Contains("s111", t.Datasets);
        Assert.DoesNotContain(t.Findings, f => f.Code == "data.no_current_coverage");
    }

    [Fact]
    public async Task Stretches_past_the_forecast_are_reported()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111("s111"));
        var tool = new CheckRouteTool(catalog, null, new RouteCheckSourceCache(new ProjNetCrsTransformFactory()), TimeProvider.System);
        var line = new GeoPolyline([new GeoPoint(0.02, 0.0), new GeoPoint(0.02, 0.03)]);

        var result = await tool.InvokeAsync(
            new CheckRouteRequest(Polyline: line, Departure: InRange.AddDays(1), Vessel: new VesselArguments(SpeedKnots: 2, SpeedThroughWater: true)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.True(value!.Currents!.WithoutCurrentNm > 1.7);
        var finding = Assert.Single(value.Findings, f => f.Code == "data.no_current_coverage");
        Assert.Equal("info", finding.Group);
    }

    private sealed class Steady(string id, double knots, double set) : IRouteCurrentSource, IRouteCurrentSeries
    {
        public RouteCurrentSourceInfo Source { get; } = new(id, null, RouteCurrentOrigin.Prediction, null);

        public RouteCurrent? At(DateTimeOffset time) => new RouteCurrent(knots, set);

        public IRouteCurrentSeries? Locate(GeoPosition position) => this;

        public IRouteCurrentStation? FindStation(string idOrName) => null;
    }
}
