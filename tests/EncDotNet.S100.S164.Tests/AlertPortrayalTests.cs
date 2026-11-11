using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// The route check's alert portrayal (#1127, slice 2): a cell portrays just
/// the features asked for, at the check's safety contour, and the chart
/// source tags the corridor's features with the alerts they raise.
/// </summary>
public sealed class AlertPortrayalTests
{
    private const string Cell = "10100AA_NAVHZ.000";

    [Fact]
    public async Task A_cell_portrays_only_the_features_asked_for_at_the_safety_contour()
    {
        var ct = TestContext.Current.CancellationToken;
        var processor = await S164Portrayal.OpenAsync("NavigationalHazards", Cell, ct);
        var alerts = ((S101DatasetData)processor.CreateLoadedData()).Alerts!;

        // 105 and 106 are the 20.1 m wrecks: a NavHazard only once the
        // safety contour is deeper than they are (UDWHAZ05).
        var at10 = await alerts.AlertsAsync(["105", "106"], Settings(10), ct);
        var at51 = await alerts.AlertsAsync(["105", "106", "64"], Settings(51), ct);

        Assert.Equal(["105", "106"], at10.Keys.Order());
        Assert.All(at10.Values, a => Assert.Empty(a));
        Assert.Equal(["105", "106", "64"], at51.Keys.Order());
        Assert.All(at51.Values, a => Assert.Contains(a, x => x.Alert.Reference == "NavHazard"));
    }

    [Fact]
    public async Task The_corridor_features_carry_their_alerts()
    {
        var ct = TestContext.Current.CancellationToken;
        var datasets = await S164RouteCheck.LoadAsync(ct, "NavigationalHazards");
        var charts = CatalogRouteCheckSourcesCharts(datasets);
        var waypoints = S164RouteCheck.NamedWaypoints(datasets.Single()).Values.ToList();
        var bounds = datasets.Single().Bounds;
        var ring = new List<GeoPosition>
        {
            new(bounds.SouthLatitude, bounds.WestLongitude),
            new(bounds.SouthLatitude, bounds.EastLongitude),
            new(bounds.NorthLatitude, bounds.EastLongitude),
            new(bounds.NorthLatitude, bounds.WestLongitude),
            new(bounds.SouthLatitude, bounds.WestLongitude),
        };
        Assert.NotEmpty(waypoints);

        var features = charts.FeaturesInCorridor(ring, bounds, new RouteChartQuery(10, 10));

        // The aids to navigation and structures the plots indicate come back
        // for their alerts alone.
        var alerted = features.Where(f => f.Kind == RouteChartFeatureKind.Alerted).ToList();
        Assert.Contains(alerted, f => f.FeatureType == "LateralBuoy" && f.Alerts.Any(a => a.Alert == RouteFindingAlert.NavHazard));
        Assert.Contains(alerted, f => f.FeatureType == "Pile");
        Assert.All(alerted, f => Assert.NotEmpty(f.Alerts));

        // A wreck shallower than the 10 m contour raises a NavHazard; the
        // 20.1 m ones don't.
        Assert.Contains(features, f => f.Kind == RouteChartFeatureKind.Danger && f.Alerts.Any(a => a.Alert == RouteFindingAlert.NavHazard));
        Assert.All(features.Where(f => f.FeatureId is "105" or "106"), f => Assert.Empty(f.Alerts));

        // A depth area shallower than the contour raises SafetyContour.
        Assert.Contains(features, f => f.Kind == RouteChartFeatureKind.DepthArea && f.Alerts.Any(a => a.Alert == RouteFindingAlert.SafetyContour));
    }

    private static IRouteChartSource CatalogRouteCheckSourcesCharts(IReadOnlyList<LoadedDataset> datasets) =>
        CatalogRouteCheckSources.FromDatasets(datasets, new ProjNetCrsTransformFactory()).Charts!;

    private static MarinerSettings Settings(double safetyContour) => MarinerSettings.Default with
    {
        SafetyContour = Depth.FromMetres(safetyContour),
        SafetyDepth = Depth.FromMetres(30),
    };
}
