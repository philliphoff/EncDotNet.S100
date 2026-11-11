using System.Text;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="AlertCatalogueReader"/> over the S-101 alert catalogue the
/// portrayal catalogue ships with (#1127).
/// </summary>
public sealed class AlertCatalogueReaderTests
{
    private static async Task<AlertCatalogue> S101Async()
    {
        using var source = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(source, cancellationToken: TestContext.Current.CancellationToken);
        return (await provider.ReadAlertCatalogueAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task S101_has_the_safety_contour_prohibited_area_and_hazard_alerts()
    {
        var catalogue = await S101Async();

        Assert.Equal(["SafetyContour", "ProhAre", "NavHazard"], catalogue.Alerts.Select(a => a.Id));
        Assert.Equal("Navigation hazards", catalogue.FindAlert("NavHazard")!.Name);
    }

    [Fact]
    public async Task Route_planning_raises_each_alert_as_an_indication()
    {
        var catalogue = await S101Async();

        Assert.All(catalogue.Alerts, a =>
        {
            Assert.Equal(AlertPriority.Indication, a.RoutePlan!.DefaultPriority);
            Assert.Equal([AlertPriority.Indication], a.RoutePlan.Priorities);
        });
        Assert.Equal("Crossing safety contour", catalogue.Messages[catalogue.FindAlert("SafetyContour")!.RoutePlan!.MessageId!].Text);
    }

    [Fact]
    public async Task Route_monitoring_priorities_and_their_defaults()
    {
        var catalogue = await S101Async();

        var safetyContour = catalogue.FindAlert("SafetyContour")!.RouteMonitor!;
        Assert.Equal(AlertPriority.Alarm, safetyContour.DefaultPriority);

        var hazard = catalogue.FindAlert("NavHazard")!.RouteMonitor!;
        Assert.Equal([AlertPriority.Warning, AlertPriority.Caution, AlertPriority.Indication], hazard.Priorities);
        Assert.Equal(AlertPriority.Warning, hazard.DefaultPriority);
        Assert.Equal(AlertPriority.Caution, catalogue.FindAlert("ProhAre")!.RouteMonitor!.DefaultPriority);
    }

    [Fact]
    public async Task The_prohibited_area_highlight_has_a_viewing_group_per_kind_of_area()
    {
        var catalogue = await S101Async();

        var highlight = catalogue.FindHighlight(catalogue.FindAlert("ProhAre")!.RoutePlan!.HighlightId!)!;
        Assert.Equal(Enumerable.Range(53012, 13), highlight.ViewingGroups);
        Assert.Equal("CautionHighlight", highlight.Style);
        Assert.False(highlight.Optional);
        Assert.True(catalogue.FindHighlight("SafetyContourHighlight")!.Optional);
    }

    [Fact]
    public void A_file_that_isnt_an_alert_catalogue_is_rejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<portrayalCatalog/>"));

        Assert.Throws<System.Xml.XmlException>(() => AlertCatalogueReader.Read(stream));
    }
}
