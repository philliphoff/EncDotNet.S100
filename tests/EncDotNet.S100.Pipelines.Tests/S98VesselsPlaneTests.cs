using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The <see cref="S98DisplayPlane.Vessels"/> stopgap (data source design note
/// D6): vessels from a data source draw above the chart and the mariner's
/// data and below ECDIS alerts, whatever the dataset order.
/// </summary>
public sealed class S98VesselsPlaneTests
{
    private static SubLayerStackItem Item(string dataset, S98DisplayPlane plane) =>
        new(new SyntheticStackPayload(dataset), plane, WithinPlanePriority: 0, SourceDatasetId: dataset);

    [Fact]
    public void Vessels_draw_above_the_chart_and_mariner_data_and_below_alerts_in_any_order()
    {
        IReadOnlyList<SubLayerStackItem>[] datasets =
        [
            [Item("ais", S98DisplayPlane.Vessels)],
            [Item("enc", S98DisplayPlane.BaseChartUnder), Item("enc-lines", S98DisplayPlane.BaseChartOver)],
            [Item("currents", S98DisplayPlane.DynamicArrows)],
            [Item("route", S98DisplayPlane.MarinerOverlay)],
            [Item("alerts", S98DisplayPlane.EcdisAlerts)],
        ];

        foreach (var order in new[] { datasets, [.. datasets.Reverse()] })
        {
            var keys = LayerStackBuilder.Build(new InteroperabilityAuthority(), order)
                .Select(item => item.SourceDatasetId)
                .ToList();

            Assert.Equal(["enc", "enc-lines", "currents", "route", "ais", "alerts"], keys);
        }
    }
}
