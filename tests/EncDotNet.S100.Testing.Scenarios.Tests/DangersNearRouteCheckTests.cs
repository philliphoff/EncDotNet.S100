using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// "Dangers near route" end to end (#955): generated S-101 cells with point
/// dangers and an S-102 surface, read by the real readers into the
/// route-check adapter and <see cref="RouteCheckEngine"/>, give the dangers
/// and shoals the world model predicts (<see cref="RouteHazards"/>).
/// </summary>
public sealed class DangersNearRouteCheckTests(DangersNearRouteFixture fixture) : IClassFixture<DangersNearRouteFixture>
{
    private const string Harbour = "101ZZ00SPIT5";
    private const string Coastal = "101ZZ00SPIT3";
    private const string Surface = "102ZZ00SPIT";

    // Safety contour and safety depth 10 m.
    private const double Contour = 10;

    // Local metres and the engine's geodesic metres differ a little over the route.
    private const double DistanceTolerance = 10;

    private readonly RouteCheckSources _sources = ScenarioLoading.Sources(fixture.Scenario);

    private ScenarioDefinition Definition => fixture.Definition;

    private S421RouteSpec Spec => Definition.Routes.Single(r => r.DatasetName == "421ZZ00DNGR");

    private double Keel => Spec.Passage.DraftMeters + Spec.Passage.SquatMeters + Spec.Passage.RequiredUkcMeters;

    [Fact]
    public void Without_a_draft_the_dangers_are_the_ones_the_world_model_expects()
    {
        var truth = RouteHazards.For(Definition, Spec, RouteCheckOptions.Default.DangerNearbyMeters, Contour, Contour, keelMeters: null);

        AssertDangers(truth, Check());

        // The scenario covers each case.
        Assert.Contains(truth, t => t.Expected && t.Relation == HazardRouteRelation.InCorridor && t.Threshold == HazardThreshold.SafetyDepth);
        Assert.Contains(truth, t => t.Expected && t.Relation == HazardRouteRelation.InCorridor && t.Threshold == HazardThreshold.NoDepth);
        Assert.Contains(truth, t => t.Expected && t.Relation == HazardRouteRelation.Near);
        Assert.Contains(truth, t => t.Threshold != HazardThreshold.None && t.Relation == HazardRouteRelation.Off);
        Assert.Contains(truth, t => t.Threshold != HazardThreshold.None && !t.Isolated && t.Relation == HazardRouteRelation.InCorridor);
        Assert.Contains(truth, t => t.Threshold == HazardThreshold.None && t.Relation == HazardRouteRelation.InCorridor);
    }

    [Fact]
    public void With_the_draft_the_keel_threshold_fires_too()
    {
        var without = RouteHazards.For(Definition, Spec, RouteCheckOptions.Default.DangerNearbyMeters, Contour, Contour, keelMeters: null);
        var with = RouteHazards.For(Definition, Spec, RouteCheckOptions.Default.DangerNearbyMeters, Contour, Contour, Keel);
        Assert.Equal(12, Keel, 9);

        var findings = AssertDangers(with, Check(withDraft: true));

        // The 11 m obstruction to port is a danger only for the draft.
        var obstruction = with.Single(t => t.Hazard.Kind == HazardKind.Obstruction && t.Hazard.ValueOfSounding == 11);
        Assert.False(without.Single(t => t.Hazard == obstruction.Hazard).Expected);
        Assert.Equal(HazardThreshold.Keel, obstruction.Threshold);
        var finding = findings.Single(f => Near(f, obstruction.Hazard));
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.EndsWith("shallower than draft + squat + UKC (12 m)", finding.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shoal_is_one_safety_contour_crossing_where_the_world_says()
    {
        var shoal = Assert.Single(RouteHazards.ShoalsAlong(Definition, Spec, Contour));
        var findings = Check();

        // The harbour cell contours at 25 m, from 5 m to 10 m on the shoal.
        var crossing = Assert.Single(findings, f => f.Code == RouteFindingCodes.CrossesSafetyContour);
        Assert.Equal(RouteFindingGroup.MustReview, crossing.Group);
        Assert.Equal(Harbour, crossing.Feature!.DatasetId);
        Assert.Equal(shoal.Leg, crossing.LegIndex);
        Assert.InRange(crossing.Location!.StartDistanceMeters, shoal.StartMeters - 30, shoal.StartMeters + 30);
        Assert.InRange(crossing.Location.EndDistanceMeters, shoal.EndMeters - 30, shoal.EndMeters + 30);
        Assert.Contains("· DepthArea · least 5.0 m · shallower than safety contour 10 m", crossing.Meta, StringComparison.Ordinal);

        // The S-102 surface's 50 m cells.
        var s102 = Assert.Single(findings, f => f.Code == RouteFindingCodes.CrossesS102Shoal);
        Assert.Equal(RouteFindingGroup.MustReview, s102.Group);
        Assert.InRange(s102.Location!.StartDistanceMeters, shoal.StartMeters - 60, shoal.StartMeters + 60);
        Assert.InRange(s102.Location.EndDistanceMeters, shoal.EndMeters - 60, shoal.EndMeters + 60);
        Assert.Matches($@"least \d+\.\d m(?: ± \d+\.\d m)? at cell \d+,\d+ of {Surface} · shallower than safety contour 10 m$", s102.Meta);
        var least = double.Parse(s102.Meta.Split("least ")[1].Split(" m")[0], CultureInfo.InvariantCulture);
        Assert.InRange(least, shoal.LeastDepthMeters - 0.5, Contour);

        // Nothing but the dangers and the shoal.
        Assert.All(findings, f => Assert.Contains(f.Code, new[]
        {
            RouteFindingCodes.DangerInCorridor, RouteFindingCodes.DangerNearRoute, RouteFindingCodes.CrossesSafetyContour, RouteFindingCodes.CrossesS102Shoal,
        }));
    }

    [Fact]
    public void Shoal_soundings_count_only_shallower_than_the_safety_depth_and_inside_the_limits()
    {
        // At 14 m the route's 13–14 m soundings count; at 10 m none does.
        var findings = Check(safetyDepth: 14).Where(f => f.Code == RouteFindingCodes.SoundingsBelowSafetyDepth).ToList();

        Assert.NotEmpty(findings);
        Assert.All(findings, f =>
        {
            Assert.Equal(RouteFindingGroup.Check, f.Group);
            Assert.Equal(Harbour, f.Feature!.DatasetId);
            Assert.Contains("shallower than safety depth 14 m", f.Meta, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(Check(), f => f.Code == RouteFindingCodes.SoundingsBelowSafetyDepth);
    }

    // #947: the coastal cell charts a wreck the harbour cell doesn't, and
    // generalises the shoal wider; the harbour cell is the finer everywhere
    // the corridor and its nearby band reach.
    [Fact]
    public void Coarse_cell_generalisation_adds_nothing_where_the_harbour_cell_charts()
    {
        var coastalWreck = Definition.Hazards.Single(h => h.Cells is not null);
        var withBoth = Check();
        var coastalOnly = Check(Contour, false, Coastal);

        Assert.All(withBoth.Where(f => f.Feature is not null), f => Assert.Equal(Harbour, f.Feature!.DatasetId));
        Assert.DoesNotContain(withBoth, f => Near(f, coastalWreck));

        var wreck = Assert.Single(coastalOnly, f => Near(f, coastalWreck));
        Assert.Equal(RouteFindingCodes.DangerInCorridor, wreck.Code);
        Assert.Equal(Coastal, wreck.Feature!.DatasetId);

        var fine = withBoth.Single(f => f.Code == RouteFindingCodes.CrossesSafetyContour).Location!;
        var coarse = coastalOnly.Single(f => f.Code == RouteFindingCodes.CrossesSafetyContour).Location!;
        Assert.True(coarse.EndDistanceMeters - coarse.StartDistanceMeters > fine.EndDistanceMeters - fine.StartDistanceMeters + 100,
            $"The coastal cell's shoal is {coarse.EndDistanceMeters - coarse.StartDistanceMeters:0} m long and the harbour cell's {fine.EndDistanceMeters - fine.StartDistanceMeters:0} m.");
    }

    // Each expected danger is one finding at its position, in the right group,
    // with its side and threshold in the meta line; there are no others.
    private List<RouteFinding> AssertDangers(IReadOnlyList<RouteHazardTruth> truth, IReadOnlyList<RouteFinding> findings)
    {
        var dangers = findings.Where(f => f.Code == RouteFindingCodes.DangerInCorridor || f.Code == RouteFindingCodes.DangerNearRoute).ToList();
        var expected = truth.Where(t => t.Expected).ToList();
        Assert.Equal(expected.Count, dangers.Count);
        foreach (var t in expected)
        {
            var finding = Assert.Single(dangers, f => Near(f, t.Hazard));
            var inCorridor = t.Relation == HazardRouteRelation.InCorridor;
            Assert.Equal(inCorridor ? RouteFindingCodes.DangerInCorridor : RouteFindingCodes.DangerNearRoute, finding.Code);
            Assert.Equal(inCorridor ? RouteFindingGroup.MustReview : RouteFindingGroup.Check, finding.Group);
            Assert.Equal(Harbour, finding.Feature!.DatasetId);
            Assert.Equal(t.Leg, finding.LegIndex);
            Assert.InRange(finding.Distance!.Value.TotalMetres, t.DistanceMeters - DistanceTolerance, t.DistanceMeters + DistanceTolerance);
            Assert.Contains(" · " + t.Hazard.FeatureType + " · ", finding.Meta, StringComparison.Ordinal);
            var side = Math.Abs(t.OffsetMeters) < 0.5 ? "on the track"
                : string.Format(CultureInfo.InvariantCulture, t.OffsetMeters > 0 ? "{0:0} m to port" : "{0:0} m to starboard", Math.Abs(t.OffsetMeters));
            Assert.Contains(side, finding.Meta, StringComparison.Ordinal);
            Assert.EndsWith(t.Threshold switch
            {
                HazardThreshold.Keel => string.Format(CultureInfo.InvariantCulture, "shallower than draft + squat + UKC ({0:0.#} m)", Keel),
                HazardThreshold.SafetyDepth => "shallower than safety depth 10 m",
                _ => t.Hazard.WaterLevelEffect == 5 ? "awash" : "depth unknown",
            }, finding.Meta, StringComparison.Ordinal);
        }

        return dangers;
    }

    private bool Near(RouteFinding finding, PointHazard hazard)
    {
        if (finding.Location is not { IsPoint: true } location)
        {
            return false;
        }

        var position = Definition.World.Frame.ToGeo(hazard.Position.X, hazard.Position.Y);
        return Math.Abs(location.Path[0].Latitude - position.Latitude) < 1e-6 && Math.Abs(location.Path[0].Longitude - position.Longitude) < 1e-6;
    }

    private IReadOnlyList<RouteFinding> Check(double safetyDepth = Contour, bool withDraft = false, params string[] only)
    {
        var sources = only.Length == 0
            ? _sources
            : CatalogRouteCheckSources.FromDatasets(ScenarioLoading.Catalog(fixture.Scenario).Datasets.Where(d => only.Contains(d.Id.Value)), new ProjNetCrsTransformFactory());
        var options = new RouteCheckOptions
        {
        };
        return new RouteCheckEngine(sources.Charts!, options, sources.Depth).Check(new PassagePlan(Legs(withDraft)), PassageLegs.Settings(new DateTimeOffset(Spec.Departure, TimeSpan.Zero), Contour, safetyDepth), TestContext.Current.CancellationToken).Findings;
    }

    private List<PassageLeg> Legs(bool withDraft)
    {
        var positions = S421RouteBuilder.Positions(Definition, Spec);
        var passage = Spec.Passage;
        return positions.Zip(positions.Skip(1), (a, b) => withDraft
            ? PassageLegs.Leg([a, b], passage.DraftMeters, squat: passage.SquatMeters, ukc: passage.RequiredUkcMeters, halfWidth: S421RouteBuilder.CrossTrackLimitMeters)
            : PassageLegs.Leg([a, b], halfWidth: S421RouteBuilder.CrossTrackLimitMeters)).ToList();
    }
}
