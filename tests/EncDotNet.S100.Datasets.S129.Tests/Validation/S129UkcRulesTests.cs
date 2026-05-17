using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S129.DataModel;
using EncDotNet.S100.Datasets.S129.Validation;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S129.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S129UkcRules"/>. Each test constructs a
/// minimal synthetic <see cref="S129UnderKeelClearancePlan"/> in memory
/// (no GML fixtures) and asserts the rule fires (or doesn't) with the
/// expected finding shape.
/// </summary>
public class S129UkcRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static readonly S129Dataset EmptyDataset = new()
    {
        Features = ImmutableArray<S129Feature>.Empty,
    };

    private static S129UkcPlanMetadata PlanMeta(
        string id = "PLAN-1",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        double? maximumDraught = null) => new()
    {
        Id = id,
        FixedTimeRange = (start is null && end is null)
            ? null
            : new S129TimeRange { Start = start, End = end },
        MaximumDraught = maximumDraught,
    };

    private static S129ControlPoint ControlPoint(
        string id,
        DateTimeOffset? time = null,
        double lat = 0,
        double lon = 0,
        bool withPosition = true,
        double? distance = null,
        double? speed = null) => new()
    {
        Id = id,
        ExpectedPassingTime = time,
        ExpectedPassingSpeed = speed,
        DistanceAboveUkcLimit = distance,
        Position = withPosition ? new GeoPosition(lat, lon) : null,
    };

    private static S129UkcPlanArea PlanArea(
        string id,
        ImmutableArray<GeoPosition> coords,
        ImmutableArray<ImmutableArray<GeoPosition>>? holes = null) => new()
    {
        Id = id,
        GeometryKind = S129GeometryKind.Surface,
        Coordinates = coords,
        InteriorRings = holes ?? ImmutableArray<ImmutableArray<GeoPosition>>.Empty,
    };

    private static S129NonNavigableArea NonNav(
        string id,
        ImmutableArray<GeoPosition> coords) => new()
    {
        Id = id,
        GeometryKind = S129GeometryKind.Surface,
        Coordinates = coords,
        InteriorRings = ImmutableArray<ImmutableArray<GeoPosition>>.Empty,
    };

    private static S129AlmostNonNavigableArea AlmostNonNav(
        string id,
        ImmutableArray<GeoPosition> coords) => new()
    {
        Id = id,
        GeometryKind = S129GeometryKind.Surface,
        Coordinates = coords,
        InteriorRings = ImmutableArray<ImmutableArray<GeoPosition>>.Empty,
    };

    private static S129UnderKeelClearancePlan Plan(
        S129UkcPlanMetadata? meta = null,
        S129UkcPlanArea? area = null,
        ImmutableArray<S129ControlPoint>? controlPoints = null,
        ImmutableArray<S129NonNavigableArea>? nonNav = null,
        ImmutableArray<S129AlmostNonNavigableArea>? almostNonNav = null) => new()
    {
        Plan = meta,
        PlanArea = area,
        ControlPoints = controlPoints ?? ImmutableArray<S129ControlPoint>.Empty,
        NonNavigableAreas = nonNav ?? ImmutableArray<S129NonNavigableArea>.Empty,
        AlmostNonNavigableAreas = almostNonNav ?? ImmutableArray<S129AlmostNonNavigableArea>.Empty,
        Source = EmptyDataset,
    };

    private static DateTimeOffset T(int minute) =>
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(minute);

    // ── S129-R-1.1 — plan validity period ─────────────────────────

    [Fact]
    public void PlanValidityPeriod_Passes_WhenNoRange()
    {
        var plan = Plan(meta: PlanMeta());
        Assert.Empty(S129UkcRules.PlanValidityPeriod.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void PlanValidityPeriod_Passes_WhenOnlyOneBoundPresent()
    {
        var plan = Plan(meta: PlanMeta(start: T(0)));
        Assert.Empty(S129UkcRules.PlanValidityPeriod.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void PlanValidityPeriod_Passes_WhenStartBeforeEnd()
    {
        var plan = Plan(meta: PlanMeta(start: T(0), end: T(30)));
        Assert.Empty(S129UkcRules.PlanValidityPeriod.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void PlanValidityPeriod_Passes_WhenStartEqualsEnd()
    {
        var plan = Plan(meta: PlanMeta(start: T(0), end: T(0)));
        Assert.Empty(S129UkcRules.PlanValidityPeriod.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void PlanValidityPeriod_Fails_WhenInverted()
    {
        var plan = Plan(meta: PlanMeta(id: "PLAN-X", start: T(30), end: T(0)));
        var f = Assert.Single(
            S129UkcRules.PlanValidityPeriod.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-1.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("PLAN-X", f.RelatedFeatureId);
    }

    // ── S129-R-2.1 — control-point time monotonicity ─────────────

    [Fact]
    public void ControlPointTimeMonotonic_Passes_OnStrictlyIncreasing()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1", time: T(0)),
            ControlPoint("CP2", time: T(5)),
            ControlPoint("CP3", time: T(10))));
        Assert.Empty(S129UkcRules.ControlPointTimeMonotonic.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void ControlPointTimeMonotonic_Passes_WhenSomeHaveNoTime()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1", time: T(0)),
            ControlPoint("CP2"),
            ControlPoint("CP3", time: T(10))));
        Assert.Empty(S129UkcRules.ControlPointTimeMonotonic.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void ControlPointTimeMonotonic_Fails_OnDuplicateTime()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1", time: T(5)),
            ControlPoint("CP2", time: T(5))));
        var f = Assert.Single(
            S129UkcRules.ControlPointTimeMonotonic.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-2.1", f.RuleId);
        Assert.Equal("CP2", f.RelatedFeatureId);
    }

    [Fact]
    public void ControlPointTimeMonotonic_Fails_OnBackwardsStep()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1", time: T(10)),
            ControlPoint("CP2", time: T(5))));
        var f = Assert.Single(
            S129UkcRules.ControlPointTimeMonotonic.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Contains("CP1", f.Message);
        Assert.Contains("CP2", f.Message);
    }

    // ── S129-R-3.1 — WGS-84 coordinate range ─────────────────────

    [Fact]
    public void CoordinatesInWgs84Range_Passes_OnValidCoordinates()
    {
        var plan = Plan(
            controlPoints: ImmutableArray.Create(
                ControlPoint("CP1", lat: 10, lon: 20),
                ControlPoint("CP2", lat: -45, lon: 178)),
            area: PlanArea("PA", ImmutableArray.Create(
                new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1))));
        Assert.Empty(S129UkcRules.CoordinatesInWgs84Range.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnControlPointOutOfRange()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1", lat: 91, lon: 0)));
        var f = Assert.Single(
            S129UkcRules.CoordinatesInWgs84Range.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-3.1", f.RuleId);
        Assert.Equal("CP1", f.RelatedFeatureId);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnPlanAreaOutOfRange()
    {
        var plan = Plan(area: PlanArea("PA", ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(0, 200), new GeoPosition(1, 1))));
        var findings = S129UkcRules.CoordinatesInWgs84Range
            .Evaluate(plan, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("PA", f.RelatedFeatureId);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnNonNavigableAreaOutOfRange()
    {
        var plan = Plan(nonNav: ImmutableArray.Create(
            NonNav("NN1", ImmutableArray.Create(
                new GeoPosition(-95, 0), new GeoPosition(0, 0), new GeoPosition(1, 1)))));
        var f = Assert.Single(
            S129UkcRules.CoordinatesInWgs84Range.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("NN1", f.RelatedFeatureId);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnAlmostNonNavigableAreaOutOfRange()
    {
        var plan = Plan(almostNonNav: ImmutableArray.Create(
            AlmostNonNav("AN1", ImmutableArray.Create(
                new GeoPosition(0, -181), new GeoPosition(0, 0), new GeoPosition(1, 1)))));
        var f = Assert.Single(
            S129UkcRules.CoordinatesInWgs84Range.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("AN1", f.RelatedFeatureId);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnInteriorRingOutOfRange()
    {
        var holes = ImmutableArray.Create(
            ImmutableArray.Create(
                new GeoPosition(0, 0), new GeoPosition(0, 0.5), new GeoPosition(95, 0.5)));
        var plan = Plan(area: PlanArea(
            "PA",
            ImmutableArray.Create(new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1)),
            holes));
        var f = Assert.Single(
            S129UkcRules.CoordinatesInWgs84Range.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("PA", f.RelatedFeatureId);
    }

    // ── S129-R-3.2 — plan-area geometry populated ────────────────

    [Fact]
    public void PlanAreaGeometryPopulated_Passes_WhenNoPlanArea()
    {
        var plan = Plan();
        Assert.Empty(S129UkcRules.PlanAreaGeometryPopulated.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void PlanAreaGeometryPopulated_Passes_WhenThreeDistinctVertices()
    {
        var plan = Plan(area: PlanArea("PA", ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1), new GeoPosition(0, 0))));
        Assert.Empty(S129UkcRules.PlanAreaGeometryPopulated.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void PlanAreaGeometryPopulated_Fails_WhenEmpty()
    {
        var plan = Plan(area: PlanArea("PA", ImmutableArray<GeoPosition>.Empty));
        var f = Assert.Single(
            S129UkcRules.PlanAreaGeometryPopulated.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-3.2", f.RuleId);
        Assert.Equal("PA", f.RelatedFeatureId);
    }

    [Fact]
    public void PlanAreaGeometryPopulated_Fails_WhenAllVerticesIdentical()
    {
        var plan = Plan(area: PlanArea("PA", ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(0, 0), new GeoPosition(0, 0))));
        var f = Assert.Single(
            S129UkcRules.PlanAreaGeometryPopulated.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Contains("1 distinct", f.Message);
    }

    // ── S129-R-4.1 — maximum draught positive ────────────────────

    [Fact]
    public void MaximumDraughtPositive_Passes_WhenAbsent()
    {
        var plan = Plan(meta: PlanMeta());
        Assert.Empty(S129UkcRules.MaximumDraughtPositive.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void MaximumDraughtPositive_Passes_WhenPositive()
    {
        var plan = Plan(meta: PlanMeta(maximumDraught: 8.5));
        Assert.Empty(S129UkcRules.MaximumDraughtPositive.Evaluate(plan, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.0)]
    public void MaximumDraughtPositive_Fails_WhenZeroOrNegative(double value)
    {
        var plan = Plan(meta: PlanMeta(id: "PLAN-Z", maximumDraught: value));
        var f = Assert.Single(
            S129UkcRules.MaximumDraughtPositive.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-4.1", f.RuleId);
        Assert.Equal("PLAN-Z", f.RelatedFeatureId);
    }

    [Fact]
    public void MaximumDraughtPositive_Fails_WhenNonFinite()
    {
        var plan = Plan(meta: PlanMeta(maximumDraught: double.NaN));
        var f = Assert.Single(
            S129UkcRules.MaximumDraughtPositive.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-4.1", f.RuleId);
    }

    // ── S129-R-5.1 — measurements finite ─────────────────────────

    [Fact]
    public void ControlPointMeasurementsFinite_Passes_OnFiniteValues()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1", distance: 1.5, speed: 10.0),
            ControlPoint("CP2", distance: -0.2, speed: 0.0)));
        Assert.Empty(
            S129UkcRules.ControlPointMeasurementsFinite.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void ControlPointMeasurementsFinite_Passes_WhenMeasurementsAbsent()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(ControlPoint("CP1")));
        Assert.Empty(
            S129UkcRules.ControlPointMeasurementsFinite.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void ControlPointMeasurementsFinite_Fails_OnNaNDistance()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP-NAN", distance: double.NaN)));
        var f = Assert.Single(
            S129UkcRules.ControlPointMeasurementsFinite
                .Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-5.1", f.RuleId);
        Assert.Equal("CP-NAN", f.RelatedFeatureId);
        Assert.Contains("distanceAboveUKCLimit", f.Message);
    }

    [Fact]
    public void ControlPointMeasurementsFinite_Fails_OnInfiniteSpeed()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP-INF", speed: double.PositiveInfinity)));
        var f = Assert.Single(
            S129UkcRules.ControlPointMeasurementsFinite
                .Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Contains("expectedPassingSpeed", f.Message);
    }

    [Fact]
    public void ControlPointMeasurementsFinite_ReportsBothMeasurementsSeparately()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP-BAD", distance: double.NaN, speed: double.NegativeInfinity)));
        var findings = S129UkcRules.ControlPointMeasurementsFinite
            .Evaluate(plan, ValidationContext.Default).ToList();
        Assert.Equal(2, findings.Count);
    }

    // ── S129-R-5.2 — control point has position ──────────────────

    [Fact]
    public void ControlPointHasPosition_Passes_WhenAllHavePositions()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1"), ControlPoint("CP2", lat: 1, lon: 2)));
        Assert.Empty(S129UkcRules.ControlPointHasPosition.Evaluate(plan, ValidationContext.Default));
    }

    [Fact]
    public void ControlPointHasPosition_Fails_WhenMissing()
    {
        var plan = Plan(controlPoints: ImmutableArray.Create(
            ControlPoint("CP1"),
            ControlPoint("CP-NOPOS", withPosition: false)));
        var f = Assert.Single(
            S129UkcRules.ControlPointHasPosition.Evaluate(plan, ValidationContext.Default).ToList());
        Assert.Equal("S129-R-5.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("CP-NOPOS", f.RelatedFeatureId);
    }

    // ── Rule set composition ─────────────────────────────────────

    [Fact]
    public void Default_ContainsSevenRules()
    {
        Assert.Equal(7, S129UkcRules.Default.Rules.Length);
        Assert.Equal(
            new[]
            {
                "S129-R-1.1", "S129-R-2.1", "S129-R-3.1", "S129-R-3.2",
                "S129-R-4.1", "S129-R-5.1", "S129-R-5.2",
            },
            S129UkcRules.Default.Rules.Select(r => r.RuleId).ToArray());
    }

    [Fact]
    public void Validate_ReturnsEmptyReport_OnClean()
    {
        var plan = Plan(
            meta: PlanMeta(start: T(0), end: T(60), maximumDraught: 10.0),
            area: PlanArea("PA", ImmutableArray.Create(
                new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(1, 1))),
            controlPoints: ImmutableArray.Create(
                ControlPoint("CP1", time: T(5), lat: 0.1, lon: 0.1, distance: 2.0, speed: 8.0),
                ControlPoint("CP2", time: T(15), lat: 0.2, lon: 0.2, distance: 1.5, speed: 8.0)));

        var report = S129UkcRules.Validate(plan);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Validate_AggregatesFindingsAcrossRules()
    {
        var plan = Plan(
            meta: PlanMeta(start: T(60), end: T(0), maximumDraught: -1.0),
            controlPoints: ImmutableArray.Create(
                ControlPoint("CP-NOPOS", withPosition: false),
                ControlPoint("CP1", time: T(5), lat: 0, lon: 0, distance: double.NaN)));

        var report = S129UkcRules.Validate(plan);

        var ruleIds = report.Findings.Select(f => f.RuleId).Distinct().OrderBy(x => x).ToArray();
        Assert.Contains("S129-R-1.1", ruleIds);
        Assert.Contains("S129-R-4.1", ruleIds);
        Assert.Contains("S129-R-5.1", ruleIds);
        Assert.Contains("S129-R-5.2", ruleIds);
    }

    [Fact]
    public void Validate_ThrowsOnNullPlan()
    {
        Assert.Throws<ArgumentNullException>(() => S129UkcRules.Validate(null!));
    }
}
