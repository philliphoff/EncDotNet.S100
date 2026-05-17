using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Datasets.S421.Validation;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S421.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S421RoutePlanRules"/>. Each test constructs
/// a minimal synthetic <see cref="S421RoutePlan"/> in memory (no GML
/// fixtures) and asserts the rule fires (or doesn't) with the expected
/// finding shape.
/// </summary>
public class S421RoutePlanRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static S421Waypoint Waypoint(string id, double lat, double lon) => new()
    {
        Id = id,
        Position = new GeoPosition(lat, lon),
        ExtraAttributes = ImmutableDictionary<string, string>.Empty,
    };

    private static S421Leg Leg(string id, double? sogMin = null, double? sogMax = null) => new()
    {
        Id = id,
        Coordinates = ImmutableArray<GeoPosition>.Empty,
        SpeedOverGroundMin = sogMin,
        SpeedOverGroundMax = sogMax,
        ExtraAttributes = ImmutableDictionary<string, string>.Empty,
    };

    private static S421ActionPoint ActionPoint(
        string id,
        ImmutableArray<GeoPosition> coords,
        S421ActionPointGeometryKind kind = S421ActionPointGeometryKind.Point) => new()
    {
        Id = id,
        GeometryKind = kind,
        Coordinates = coords,
        ExtraAttributes = ImmutableDictionary<string, string>.Empty,
    };

    private static S421RoutePlan Plan(
        ImmutableArray<S421Waypoint>? waypoints = null,
        ImmutableArray<S421Leg>? legs = null,
        ImmutableArray<S421ActionPoint>? actionPoints = null,
        int? editionNumber = null,
        string routeId = "ROUTE-1")
    {
        var route = new S421Route
        {
            Id = routeId,
            EditionNumber = editionNumber,
            Waypoints = waypoints ?? ImmutableArray<S421Waypoint>.Empty,
            Legs = legs ?? ImmutableArray<S421Leg>.Empty,
            ActionPoints = actionPoints ?? ImmutableArray<S421ActionPoint>.Empty,
            Schedules = ImmutableArray<S421Schedule>.Empty,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

        var dataset = new S421Dataset
        {
            Features = ImmutableArray<S421Feature>.Empty,
            InformationTypes = ImmutableArray<S421InformationType>.Empty,
        };

        return new S421RoutePlan { Route = route, Source = dataset };
    }

    // ── S421-R-3.1 — minimum waypoint count ──────────────────────

    [Fact]
    public void MinimumWaypointCount_Passes_WhenTwoOrMore()
    {
        var plan = Plan(waypoints:
            ImmutableArray.Create(Waypoint("W1", 0, 0), Waypoint("W2", 0, 1)));
        var findings = S421RoutePlanRules.MinimumWaypointCount.Evaluate(plan, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MinimumWaypointCount_Fails_WhenFewerThanTwo(int count)
    {
        var wpts = Enumerable.Range(1, count).Select(i => Waypoint($"W{i}", 0, i)).ToImmutableArray();
        var plan = Plan(waypoints: wpts);
        var findings = S421RoutePlanRules.MinimumWaypointCount.Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S421-R-3.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("ROUTE-1", f.RelatedFeatureId);
    }

    // ── S421-R-3.2 — no coincident consecutive waypoints ─────────

    [Fact]
    public void NoCoincidentConsecutiveWaypoints_Passes_WhenDistinct()
    {
        var plan = Plan(waypoints: ImmutableArray.Create(
            Waypoint("W1", 0, 0), Waypoint("W2", 0, 1), Waypoint("W3", 1, 1)));
        var findings = S421RoutePlanRules.NoCoincidentConsecutiveWaypoints
            .Evaluate(plan, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void NoCoincidentConsecutiveWaypoints_Fails_OnCoincidentPair()
    {
        var plan = Plan(waypoints: ImmutableArray.Create(
            Waypoint("W1", 10.0, 20.0),
            Waypoint("W2", 10.0, 20.0),
            Waypoint("W3", 11.0, 21.0)));
        var findings = S421RoutePlanRules.NoCoincidentConsecutiveWaypoints
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S421-R-3.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("W2", f.RelatedFeatureId);
        Assert.Equal(new GeoPosition(10.0, 20.0), f.Point);
    }

    [Fact]
    public void NoCoincidentConsecutiveWaypoints_Tolerance_AllowsFloatingPointDrift()
    {
        var plan = Plan(waypoints: ImmutableArray.Create(
            Waypoint("W1", 10.0, 20.0),
            Waypoint("W2", 10.0 + 1e-7, 20.0)));
        var findings = S421RoutePlanRules.NoCoincidentConsecutiveWaypoints
            .Evaluate(plan, ValidationContext.Default);
        Assert.Empty(findings);
    }

    // ── S421-R-4.1 — waypoint lat/lon in range ──────────────────

    [Fact]
    public void WaypointLatLonInRange_Passes_OnValidCoordinates()
    {
        var plan = Plan(waypoints: ImmutableArray.Create(
            Waypoint("W1", -89.9, -179.9), Waypoint("W2", 89.9, 179.9)));
        var findings = S421RoutePlanRules.WaypointLatLonInRange.Evaluate(plan, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void WaypointLatLonInRange_Fails_OnOutOfRangeLatitude()
    {
        var plan = Plan(waypoints: ImmutableArray.Create(
            Waypoint("W1", 0, 0), Waypoint("BAD", 95.0, 0)));
        var findings = S421RoutePlanRules.WaypointLatLonInRange
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S421-R-4.1", f.RuleId);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Contains("latitude", f.Message);
    }

    [Fact]
    public void WaypointLatLonInRange_Fails_OnOutOfRangeLongitude()
    {
        var plan = Plan(waypoints: ImmutableArray.Create(
            Waypoint("W1", 0, 0), Waypoint("BAD", 0, 181.0)));
        var findings = S421RoutePlanRules.WaypointLatLonInRange
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("longitude", f.Message);
    }

    // ── S421-R-5.1 — leg SOG sanity ─────────────────────────────

    [Fact]
    public void LegSpeedOverGroundSane_Passes_WhenAbsentOrValid()
    {
        var plan = Plan(legs: ImmutableArray.Create(
            Leg("L1"),
            Leg("L2", sogMin: 5.0, sogMax: 12.0),
            Leg("L3", sogMax: 10.0)));
        var findings = S421RoutePlanRules.LegSpeedOverGroundSane
            .Evaluate(plan, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void LegSpeedOverGroundSane_Fails_OnNegativeMin()
    {
        var plan = Plan(legs: ImmutableArray.Create(Leg("L1", sogMin: -1.0)));
        var findings = S421RoutePlanRules.LegSpeedOverGroundSane
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("L1", f.RelatedFeatureId);
    }

    [Fact]
    public void LegSpeedOverGroundSane_Fails_WhenMinGreaterThanMax()
    {
        var plan = Plan(legs: ImmutableArray.Create(Leg("L1", sogMin: 15.0, sogMax: 10.0)));
        var findings = S421RoutePlanRules.LegSpeedOverGroundSane
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("greater than maximum", f.Message);
    }

    // ── S421-R-6.1 — route edition number ───────────────────────

    [Fact]
    public void RouteEditionNumberPositive_Passes_WhenAbsent()
    {
        var plan = Plan(editionNumber: null);
        Assert.Empty(S421RoutePlanRules.RouteEditionNumberPositive.Evaluate(plan, ValidationContext.Default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public void RouteEditionNumberPositive_Passes_OnPositiveValue(int edition)
    {
        var plan = Plan(editionNumber: edition);
        Assert.Empty(S421RoutePlanRules.RouteEditionNumberPositive.Evaluate(plan, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RouteEditionNumberPositive_Fails_OnNonPositive(int edition)
    {
        var plan = Plan(editionNumber: edition);
        var findings = S421RoutePlanRules.RouteEditionNumberPositive
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Contains(edition.ToString(), f.Message);
    }

    // ── S421-R-7.1 — action point geometry populated ────────────

    [Fact]
    public void ActionPointGeometryPopulated_Passes_WhenCoordinatesPresent()
    {
        var ap = ActionPoint("AP1", ImmutableArray.Create(new GeoPosition(0, 0)));
        var plan = Plan(actionPoints: ImmutableArray.Create(ap));
        Assert.Empty(S421RoutePlanRules.ActionPointGeometryPopulated
            .Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void ActionPointGeometryPopulated_Fails_OnEmptyCoordinates()
    {
        var ap = ActionPoint("AP_EMPTY", ImmutableArray<GeoPosition>.Empty);
        var plan = Plan(actionPoints: ImmutableArray.Create(ap));
        var findings = S421RoutePlanRules.ActionPointGeometryPopulated
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("AP_EMPTY", f.RelatedFeatureId);
    }

    // ── Default ruleset composition ─────────────────────────────

    [Fact]
    public void Default_ContainsAllSixRules()
    {
        Assert.Equal(6, S421RoutePlanRules.Default.Rules.Length);
        var ids = S421RoutePlanRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S421-R-3.1", ids);
        Assert.Contains("S421-R-3.2", ids);
        Assert.Contains("S421-R-4.1", ids);
        Assert.Contains("S421-R-5.1", ids);
        Assert.Contains("S421-R-6.1", ids);
        Assert.Contains("S421-R-7.1", ids);
    }

    [Fact]
    public void Validate_OnValidPlan_ProducesNoFindings()
    {
        var plan = Plan(
            waypoints: ImmutableArray.Create(
                Waypoint("W1", 10, 20), Waypoint("W2", 11, 21)),
            legs: ImmutableArray.Create(Leg("L1", sogMin: 5.0, sogMax: 12.0)),
            editionNumber: 1);
        var report = S421RoutePlanRules.Validate(plan);
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(6, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidPlan_AggregatesFindingsFromMultipleRules()
    {
        // Only one waypoint (3.1 fires), out-of-range lon (4.1 fires),
        // negative SOG (5.1 fires), zero edition (6.1 fires).
        var plan = Plan(
            waypoints: ImmutableArray.Create(Waypoint("W1", 0, 200)),
            legs: ImmutableArray.Create(Leg("L1", sogMin: -1.0)),
            editionNumber: 0);

        var report = S421RoutePlanRules.Validate(plan);
        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S421-R-3.1", ids);
        Assert.Contains("S421-R-4.1", ids);
        Assert.Contains("S421-R-5.1", ids);
        Assert.Contains("S421-R-6.1", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullPlan()
    {
        Assert.Throws<ArgumentNullException>(() => S421RoutePlanRules.Validate(null!));
    }
}
