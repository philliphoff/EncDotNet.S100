using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Spit across scales" with an S-102 surface that declares no vertical
/// datum, as the UKHO trial files do (#939): the clearance is unchanged, but
/// the depth and water level can't be confirmed to share a reference.
/// </summary>
public sealed class S102WithoutDatumUkcTests(SpitWithoutS102DatumFixture fixture) : IClassFixture<SpitWithoutS102DatumFixture>
{
    private static readonly DateTimeOffset At = new(new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc));

    private readonly RouteCheckSources _sources = ScenarioLoading.Sources(fixture.Scenario);

    [Fact]
    public void A_channel_sample_meets_the_setting_but_its_datum_is_unconfirmed()
    {
        var definition = fixture.Definition;
        var (x, y) = definition.Places["channel-west"];
        var leg = PassageLegs.Leg(
            [definition.World.Frame.ToGeo(x + 0.1, y + 0.1), definition.World.Frame.ToGeo(x + 1.1, y + 0.1)],
            draft: 6.0,
            speedKnots: 10,
            squat: 0.3,
            ukc: 1.0);

        var result = ScenarioChecks.Clearance(_sources, new PassagePlan([leg], At), PassageLegs.Settings(At));
        var sample = Assert.Single(result.Samples);

        Assert.Equal(ChartedDepthKind.Bathymetry, sample.Depth!.Kind);
        Assert.Null(sample.Depth.VerticalDatum);
        Assert.Equal(WaterLevelStatus.Applied, sample.WaterLevel.Status);
        Assert.Equal(DatumAgreement.Unknown, sample.WaterLevel.Datum);
        Assert.Equal(ClearanceBand.Meets, sample.Band);

        // Confidence is reported beside the band, not in place of it (#939).
        Assert.Equal(UkcState.MeetsSettings, sample.State);
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, sample.Confidence);
        Assert.Equal(ConfidenceReasons.DatumUnconfirmed, result.Confidence);
        Assert.Contains(result.LowConfidence, s => s.Kind == PassageStretchKind.DatumUnconfirmed);
    }
}
