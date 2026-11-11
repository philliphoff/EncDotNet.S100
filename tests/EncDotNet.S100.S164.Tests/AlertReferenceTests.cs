using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.S164.Tests;

/// <summary>
/// The S-101 portrayal keeps each feature's <c>AlertReference</c> on its
/// drawing instructions (#1127), for the route-planning tests' cells: the
/// alerts the Part 5 and Part 6 reference plots indicate.
/// </summary>
public sealed class AlertReferenceTests
{
    [Fact]
    public async Task NavigationalHazards_cell_marks_every_hazard_type_the_plots_indicate()
    {
        var alerts = await AlertsAsync("NavigationalHazards", "10100AA_NAVHZ.000");

        // The feature types Part5NavigationalHazardsTests expects the route
        // check to indicate once it follows the alert catalogue.
        string[] hazards =
        [
            "CardinalBuoy", "CardinalBeacon", "IsolatedDangerBuoy", "IsolatedDangerBeacon", "LateralBuoy",
            "LateralBeacon", "SafeWaterBuoy", "SafeWaterBeacon", "SpecialPurposeGeneralBuoy",
            "SpecialPurposeGeneralBeacon", "InstallationBuoy", "Daymark", "Pile", "MooringBuoy", "Dolphin",
            "LightFloat", "LightVessel", "OffshorePlatform", "OilBarrier", "IceArea", "VirtualAISAidToNavigation",
            "Landmark", "SpanFixed", "CableOverhead", "PipelineOverhead", "Conveyor", "PylonBridgeSupport",
            "LogPond", "FishingFacility",
        ];
        Assert.Empty(hazards.Except(alerts.Where(a => a.Alert.Reference == "NavHazard").Select(a => a.FeatureType)));

        // Dangers shallower than the 10 m safety contour, as UDWHAZ05 marks them.
        Assert.Contains(alerts, a => a.Alert.Reference == "NavHazard" && a.FeatureType == "Wreck");
    }

    [Fact]
    public async Task SpecialConditions_cell_marks_its_areas_with_their_kind()
    {
        var alerts = await AlertsAsync("SpecialConditions", "10100AA_ARSPC.000");

        var areas = alerts.Where(a => a.Alert.Reference == "ProhAre").ToList();
        Assert.NotEmpty(areas);

        // Each kind of area names its own ProhAre viewing group (53012–53024).
        Assert.All(areas, a => Assert.InRange(a.Alert.PlanViewingGroup ?? 0, 53012, 53024));
        Assert.Contains(areas, a => a.FeatureType == "AnchorageArea" && a.Alert.PlanViewingGroup == 53023);
    }

    private static async Task<List<(string FeatureType, PortrayalAlertReference Alert)>> AlertsAsync(string exchangeSet, string cell)
    {
        var (processor, instructions) = await S164Portrayal.PortrayAsync(
            exchangeSet, cell, S164Portrayal.DisplayTestSettings, TestContext.Current.CancellationToken);

        return instructions
            .Where(i => i.AlertReference is not null)
            .Select(i => (processor.GetFeatureInfo(i.FeatureReference)?.FeatureType ?? "", i.AlertReference!))
            .Distinct()
            .ToList();
    }
}
