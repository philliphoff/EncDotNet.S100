using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

public class RouteFindingTests
{
    private static RouteFinding Danger() => new RouteFinding("route_check.danger_in_corridor.0", RouteFindingCodes.DangerInCorridor)
    {
        LegIndex = 0,
        Distance = Length.FromMetres(400),
        Location = RouteFindingLocation.Point(new GeoPosition(0, 0.004), 400),
        Feature = new RouteFindingFeature("101HARBOUR", "5") { FeatureType = "Wreck" },
        Details = new DangerDetails
        {
            FeatureType = "Wreck",
            Depth = Depth.FromMetres(3.2),
            WaterLevelEffect = 3,
            ThresholdKind = RouteFindingThreshold.SafetyDepth,
            Threshold = Depth.FromMetres(10),
            Side = RouteFindingSide.Port,
            Offset = Length.FromMetres(40),
        },
    };

    [Fact]
    public void Every_code_is_in_the_catalogue_once()
    {
        Assert.Equal(RouteFindingCodes.All.Count, RouteFindingCodes.All.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(RouteFindingCodes.All, c =>
        {
            Assert.True(RouteFindingCodes.TryGet(c.Value, out var found));
            Assert.Same(c, found);
        });
        Assert.False(RouteFindingCodes.TryGet("data.s111_forecast_expired", out _));
    }

    [Fact]
    public void Headline_ranks_follow_decision_3()
    {
        RouteFindingCode[] order =
        [
            RouteFindingCodes.CrossesLand,
            RouteFindingCodes.CrossesDrying,
            RouteFindingCodes.EntersProhibitedArea,
            RouteFindingCodes.DangerInCorridor,
            RouteFindingCodes.BelowSetting,
            RouteFindingCodes.WarningOnRoute,
            RouteFindingCodes.CrossesSafetyContour,
        ];

        Assert.Equal(order, order.OrderBy(c => c.HeadlineRank));
        Assert.All(RouteFindingCodes.All.Where(c => c.Category == RouteFindingCategory.Area), c => Assert.Equal(2, c.HeadlineRank));
    }

    [Fact]
    public void A_finding_takes_its_category_and_default_group_from_its_code()
    {
        var finding = new RouteFinding("route_check.caution_area.0", RouteFindingCodes.CautionArea);
        var lighter = new RouteFinding("route_check.caution_area.1", RouteFindingCodes.CautionArea, RouteFindingGroup.Info);

        Assert.Equal((RouteFindingCategory.Area, RouteFindingGroup.Check), (finding.Category, finding.Group));
        Assert.Equal(RouteFindingGroup.Info, lighter.Group);
        Assert.Equal(string.Empty, finding.Title);
    }

    [Fact]
    public void The_default_formatter_writes_invariant_english_in_metres()
    {
        var finding = RouteFindingFormatter.Default.Render(Danger());

        Assert.Equal("Wreck in the corridor", finding.Title);
        Assert.Equal("Leg 1 → 2 · Wreck · 40 m to port · 3.2 m · shallower than safety depth 10 m", finding.Meta);
        Assert.Equal("route_check.danger_in_corridor.0", finding.Id);
        Assert.IsType<DangerDetails>(finding.Details);
    }

    [Fact]
    public void A_finding_can_be_rendered_again_in_another_unit_and_culture()
    {
        var metres = RouteFindingFormatter.Default.Render(Danger());

        var feet = new RouteFindingFormatter(CultureInfo.GetCultureInfo("de-DE"), DepthUnit.Feet).Render(metres);

        Assert.Equal("Leg 1 → 2 · Wreck · 40 m to port · 10,5 ft · shallower than safety depth 32,8 ft", feet.Meta);
        Assert.Equal(metres.Distance, feet.Distance);
        Assert.Same(metres.Details, feet.Details);
    }

    [Fact]
    public void Clearance_findings_carry_their_numbers()
    {
        // 11,5 m to 0,05°, then no charted depth: draft 10 leaves 1,5 m against a required 2 m.
        var plan = Fixture.Plan(Fixture.Leg(draft: 10, speed: 6, required: 2));
        var result = new PassageCheckEngine(
                new FakeDepth((_, lon) => lon < 0.05 ? FakeDepth.Bathymetry(11.5) : null),
                FakeWater.Constant(0))
            .Check(plan, Fixture.Settings(), TestContext.Current.CancellationToken);

        var findings = ClearanceFindings.Build(plan, result);

        var below = Assert.Single(findings, f => f.Code == RouteFindingCodes.BelowSetting);
        Assert.Equal(("clearance.below_setting.0", RouteFindingGroup.MustReview, RouteFindingCategory.Clearance), (below.Id, below.Group, below.Category));
        var details = Assert.IsType<ClearanceDetails>(below.Details);
        Assert.Equal(1.5, details.MinimumClearance!.Value.TotalMetres, 6);
        Assert.Equal(-0.5, details.Margin!.Value.TotalMetres, 6);
        Assert.InRange(details.MinutesBelow!.Value, 29, 31);
        Assert.StartsWith("Below your UKC setting for ", below.Title, StringComparison.Ordinal);
        Assert.StartsWith("Leg 1 → 2 · min 1.5 m at 2024-06-01 06:", below.Meta, StringComparison.Ordinal);
        Assert.False(below.Location!.IsPoint);

        var gap = Assert.Single(findings, f => f.Code == RouteFindingCodes.NoChartedDepth);
        Assert.Equal(("data.no_charted_depth.0", RouteFindingCategory.Data), (gap.Id, gap.Category));
        Assert.Equal(PassageStretchKind.NoChartedDepth, Assert.IsType<ClearanceDetails>(gap.Details).Stretch);
        Assert.Equal("No charted depth", gap.Title);
        Assert.EndsWith(" NM · no S-102 or S-101 coverage", gap.Meta, StringComparison.Ordinal);
        Assert.Equal(RouteFinding.Sort(findings), findings);
    }
}
