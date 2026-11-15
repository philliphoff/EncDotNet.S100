using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// S-104 and S-111 values labelled by how they were produced (#1505), with
/// the SoundCharts for ArcGIS wording (design brief §7.2): Object information's
/// Data row and the Legend section's detail.
/// </summary>
public sealed class DataDynamicityLabelTests
{
    private static readonly DateTime Run = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(DataDynamicity.Observation, "Observed")]
    [InlineData(DataDynamicity.AstronomicalPrediction, "Predicted · astronomical tide only, not a forecast")]
    [InlineData(DataDynamicity.AnalysisOrHybrid, "Analysis")]
    [InlineData(DataDynamicity.HydrodynamicHindcast, "Hindcast")]
    public void Each_kind_has_the_brief_s_label_and_only_a_forecast_names_its_run(DataDynamicity kind, string expected) =>
        Assert.Equal(expected, DataDynamicityText.Label(kind, Run, TimeFormat.Utc));

    [Fact]
    public void A_forecast_names_its_run_in_the_user_s_time_format()
    {
        Assert.Equal("Forecast, run " + TimeFormatting.Format(Run, TimeFormat.Utc), DataDynamicityText.Label(DataDynamicity.HydrodynamicForecast, Run, TimeFormat.Utc));
        Assert.Equal("Forecast, run " + TimeFormatting.Format(Run, TimeFormat.Local), DataDynamicityText.Label(DataDynamicity.HydrodynamicForecast, Run, TimeFormat.Local));
        Assert.Equal("Forecast", DataDynamicityText.Label(DataDynamicity.HydrodynamicForecast, null, TimeFormat.Utc));
    }

    [Fact]
    public void Object_information_shows_the_data_row_as_the_label_not_as_a_time()
    {
        var rows = PickAttributeFormatter.Format(
            [
                new PickAttribute { Code = "waterLevelHeight", Name = "Water Level Height", RawValue = "1.35", DisplayValue = "1.35 m" },
                PickAttribute.DataDynamicityRow(DataDynamicity.HydrodynamicForecast, Run),
            ],
            TimeFormat.Utc,
            DepthUnit.Metres);

        var row = Assert.Single(rows, r => r.Code == PickAttribute.DataDynamicityCode);
        Assert.Equal("Data", row.DisplayName);
        Assert.Equal("Forecast, run " + TimeFormatting.Format(Run, TimeFormat.Utc), row.DisplayText);
    }

    [Fact]
    public void Object_information_labels_a_prediction_as_not_a_forecast()
    {
        var row = Assert.Single(PickAttributeFormatter.Format(
            [PickAttribute.DataDynamicityRow(DataDynamicity.AstronomicalPrediction, Run)],
            TimeFormat.Utc,
            DepthUnit.Metres));

        Assert.Equal("Predicted · astronomical tide only, not a forecast", row.DisplayText);
    }

    [Fact]
    public void The_legend_section_says_how_its_values_were_produced()
    {
        var f = new LegendServiceTests.Fixture();
        var entry = f.Add("104US00_a.h5", "S-104", 40, -75, 41, -74);
        entry.SetDataDynamicity(DataDynamicity.HydrodynamicForecast, Run);
        f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        f.Loader.SetEntries(
            LegendServiceTests.VectorEntry("104US00_a.h5", "S-104", "s104.stations", "Water level: 104US00_a.h5", S98DisplayPlane.DynamicArrows, [new SwatchLegendEntry("#D0E8FF", "Rising")]),
            LegendServiceTests.VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, [new SwatchLegendEntry("#D0E8FF", "0–1 kn")]));
        f.Viewport.Publish(39, -76, 42, -73);
        using var service = f.Create();
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero))),
            new FixedFormat(),
            find: null);

        var s104 = Assert.Single(service.Sections, s => s.Id == "S-104");
        Assert.Equal(DataDynamicity.HydrodynamicForecast, s104.DataDynamicity);
        Assert.Equal(Run, s104.IssueTime);
        Assert.Equal(
            "104US00_a.h5 · Forecast, run " + TimeFormatting.Format(Run, TimeFormat.Utc),
            Assert.Single(tab.Sections, s => s.Id == "S-104").Detail);

        // A dataset that doesn't say has no label.
        Assert.Null(Assert.Single(service.Sections, s => s.Id == "S-111").DataDynamicity);
        Assert.Equal("111US00_a.h5", Assert.Single(tab.Sections, s => s.Id == "S-111").Detail);
    }

    private sealed class FixedFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
