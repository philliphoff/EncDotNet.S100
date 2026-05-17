using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Datasets.S122.Validation;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S122.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S122MarineProtectedAreaRules"/>. Each test
/// builds a minimal synthetic <see cref="S122MarineProtectedAreaDataset"/>
/// in memory (no GML fixtures) and asserts the corresponding rule fires
/// (or doesn't) with the expected finding shape.
/// </summary>
public class S122MarineProtectedAreaRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static S122Dataset EmptySource() => new()
    {
        Features = ImmutableArray<S122Feature>.Empty,
        InformationTypes = ImmutableArray<S122InformationType>.Empty,
    };

    private static S122MarineProtectedArea Mpa(
        string id,
        S122GeometryKind kind = S122GeometryKind.Surface,
        ImmutableArray<GeoPosition>? coords = null,
        int? scaleMinimum = null)
        => new()
        {
            Id = id,
            GeometryKind = kind,
            Coordinates = coords ?? ImmutableArray<GeoPosition>.Empty,
            ScaleMinimum = scaleMinimum,
            References = ImmutableArray<GmlReference>.Empty,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

    private static S122RestrictedArea Restricted(
        string id,
        S122GeometryKind kind = S122GeometryKind.Surface,
        ImmutableArray<GeoPosition>? coords = null)
        => new()
        {
            Id = id,
            GeometryKind = kind,
            Coordinates = coords ?? ImmutableArray<GeoPosition>.Empty,
            References = ImmutableArray<GmlReference>.Empty,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

    private static S122Authority Authority(string id) => new()
    {
        Id = id,
        References = ImmutableArray<GmlReference>.Empty,
        ExtraAttributes = ImmutableDictionary<string, string>.Empty,
    };

    private static ImmutableArray<GeoPosition> ClosedSquare(double lat, double lon, double size = 0.1)
        => ImmutableArray.Create(
            new GeoPosition(lat, lon),
            new GeoPosition(lat + size, lon),
            new GeoPosition(lat + size, lon + size),
            new GeoPosition(lat, lon + size),
            new GeoPosition(lat, lon));

    private static S122MarineProtectedAreaDataset Dataset(
        IEnumerable<IS122Feature>? features = null,
        IEnumerable<IS122InformationType>? infoTypes = null,
        string? productIdentifier = "S-122",
        string? datasetIdentifier = "DS-1")
        => new()
        {
            Features = (features ?? Array.Empty<IS122Feature>()).ToImmutableArray(),
            InformationTypes = (infoTypes ?? Array.Empty<IS122InformationType>()).ToImmutableArray(),
            ProductIdentifier = productIdentifier,
            DatasetIdentifier = datasetIdentifier,
            Source = EmptySource(),
        };

    // ── S122-R-3.1 — geometry present when kind is set ──────────

    [Fact]
    public void GeometryPresentWhenKindSet_Passes_WhenSurfaceHasCoords()
    {
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Surface, ClosedSquare(0, 0)) });
        Assert.Empty(S122MarineProtectedAreaRules.GeometryPresentWhenKindSet
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void GeometryPresentWhenKindSet_Passes_OnGeometryLessContainer()
    {
        // GeometryKind.None — tolerated (e.g. Authority-style containers).
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.None) });
        Assert.Empty(S122MarineProtectedAreaRules.GeometryPresentWhenKindSet
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void GeometryPresentWhenKindSet_Fails_WhenSurfaceMissingCoords()
    {
        var ds = Dataset(features: new[] { Mpa("BAD", S122GeometryKind.Surface, ImmutableArray<GeoPosition>.Empty) });
        var findings = S122MarineProtectedAreaRules.GeometryPresentWhenKindSet
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-3.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
    }

    [Fact]
    public void GeometryPresentWhenKindSet_Fails_WhenPointMissingCoords()
    {
        var ds = Dataset(features: new[] { Mpa("P1", S122GeometryKind.Point, ImmutableArray<GeoPosition>.Empty) });
        var findings = S122MarineProtectedAreaRules.GeometryPresentWhenKindSet
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("P1", f.RelatedFeatureId);
        Assert.Contains("Point", f.Message);
    }

    // ── S122-R-4.1 — coordinates within WGS-84 ──────────────────

    [Fact]
    public void CoordinatesWithinWgs84Range_Passes_OnValidCoords()
    {
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Surface, ClosedSquare(45, 100)) });
        Assert.Empty(S122MarineProtectedAreaRules.CoordinatesWithinWgs84Range
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesWithinWgs84Range_Fails_OnOutOfRangeLatitude()
    {
        var bad = ImmutableArray.Create(
            new GeoPosition(95.0, 0.0),
            new GeoPosition(0.0, 0.0));
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Curve, bad) });
        var findings = S122MarineProtectedAreaRules.CoordinatesWithinWgs84Range
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-4.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("F1", f.RelatedFeatureId);
        Assert.Contains("latitude", f.Message);
        Assert.Equal(new GeoPosition(95.0, 0.0), f.Point);
    }

    [Fact]
    public void CoordinatesWithinWgs84Range_Fails_OnOutOfRangeLongitude()
    {
        var bad = ImmutableArray.Create(new GeoPosition(0.0, 200.0));
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Point, bad) });
        var findings = S122MarineProtectedAreaRules.CoordinatesWithinWgs84Range
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("longitude", f.Message);
    }

    [Fact]
    public void CoordinatesWithinWgs84Range_ReportsMultipleFindingsAcrossFeatures()
    {
        var bad1 = ImmutableArray.Create(new GeoPosition(0, 200));
        var bad2 = ImmutableArray.Create(new GeoPosition(-100, 0));
        var ds = Dataset(features: new IS122Feature[]
        {
            Mpa("A", S122GeometryKind.Point, bad1),
            Restricted("B", S122GeometryKind.Point, bad2),
        });
        var ids = S122MarineProtectedAreaRules.CoordinatesWithinWgs84Range
            .Evaluate(ds, ValidationContext.Default)
            .Select(f => f.RelatedFeatureId)
            .ToHashSet();
        Assert.Contains("A", ids);
        Assert.Contains("B", ids);
    }

    // ── S122-R-5.1 — surface ring closure ───────────────────────

    [Fact]
    public void SurfaceRingClosure_Passes_OnClosedRing()
    {
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Surface, ClosedSquare(10, 20)) });
        Assert.Empty(S122MarineProtectedAreaRules.SurfaceRingClosure
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingClosure_Passes_OnNonSurfaceFeature()
    {
        // A curve with 3 points and unequal endpoints — non-surface, rule doesn't apply.
        var curve = ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(1, 1), new GeoPosition(2, 2));
        var ds = Dataset(features: new[] { Mpa("C1", S122GeometryKind.Curve, curve) });
        Assert.Empty(S122MarineProtectedAreaRules.SurfaceRingClosure
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingClosure_Fails_OnTooFewCoordinates()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(0, 1), new GeoPosition(0, 0));
        var ds = Dataset(features: new[] { Mpa("SHORT", S122GeometryKind.Surface, ring) });
        var findings = S122MarineProtectedAreaRules.SurfaceRingClosure
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-5.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("SHORT", f.RelatedFeatureId);
        Assert.Contains("at least 4", f.Message);
    }

    [Fact]
    public void SurfaceRingClosure_Fails_WhenRingNotClosed()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0),
            new GeoPosition(0, 1),
            new GeoPosition(1, 1),
            new GeoPosition(1, 0));
        var ds = Dataset(features: new[] { Mpa("OPEN", S122GeometryKind.Surface, ring) });
        var findings = S122MarineProtectedAreaRules.SurfaceRingClosure
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("OPEN", f.RelatedFeatureId);
        Assert.Contains("not closed", f.Message);
    }

    // ── S122-R-6.1 — unique feature ids ─────────────────────────

    [Fact]
    public void UniqueFeatureIds_Passes_OnDistinctIds()
    {
        var ds = Dataset(features: new IS122Feature[]
        {
            Mpa("A", S122GeometryKind.Surface, ClosedSquare(0, 0)),
            Mpa("B", S122GeometryKind.Surface, ClosedSquare(1, 1)),
        });
        Assert.Empty(S122MarineProtectedAreaRules.UniqueFeatureIds
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void UniqueFeatureIds_Fails_OnDuplicateId()
    {
        var ds = Dataset(features: new IS122Feature[]
        {
            Mpa("DUP", S122GeometryKind.Surface, ClosedSquare(0, 0)),
            Restricted("DUP", S122GeometryKind.Surface, ClosedSquare(1, 1)),
            Mpa("OK", S122GeometryKind.Surface, ClosedSquare(2, 2)),
        });
        var findings = S122MarineProtectedAreaRules.UniqueFeatureIds
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-6.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("DUP", f.RelatedFeatureId);
    }

    // ── S122-R-6.2 — unique information type ids ────────────────

    [Fact]
    public void UniqueInformationTypeIds_Passes_OnDistinctIds()
    {
        var ds = Dataset(infoTypes: new[] { Authority("AUTH-1"), Authority("AUTH-2") });
        Assert.Empty(S122MarineProtectedAreaRules.UniqueInformationTypeIds
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void UniqueInformationTypeIds_Fails_OnDuplicateId()
    {
        var ds = Dataset(infoTypes: new[] { Authority("X"), Authority("X") });
        var findings = S122MarineProtectedAreaRules.UniqueInformationTypeIds
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-6.2", f.RuleId);
        Assert.Equal("X", f.RelatedFeatureId);
    }

    // ── S122-R-7.1 — scale minimum positive ─────────────────────

    [Fact]
    public void ScaleMinimumPositive_Passes_WhenAbsent()
    {
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Surface, ClosedSquare(0, 0)) });
        Assert.Empty(S122MarineProtectedAreaRules.ScaleMinimumPositive
            .Evaluate(ds, ValidationContext.Default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50_000)]
    [InlineData(10_000_000)]
    public void ScaleMinimumPositive_Passes_OnPositiveValue(int scale)
    {
        var ds = Dataset(features: new[] { Mpa("F1", S122GeometryKind.Surface, ClosedSquare(0, 0), scaleMinimum: scale) });
        Assert.Empty(S122MarineProtectedAreaRules.ScaleMinimumPositive
            .Evaluate(ds, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ScaleMinimumPositive_Fails_OnNonPositiveValue(int scale)
    {
        var ds = Dataset(features: new[] { Mpa("BAD", S122GeometryKind.Surface, ClosedSquare(0, 0), scaleMinimum: scale) });
        var findings = S122MarineProtectedAreaRules.ScaleMinimumPositive
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-7.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Contains(scale.ToString(), f.Message);
    }

    // ── S122-R-9.1 — product identifier names S-122 ─────────────

    [Fact]
    public void ProductIdentifierIsS122_Passes_WhenAbsent()
    {
        var ds = Dataset(productIdentifier: null);
        Assert.Empty(S122MarineProtectedAreaRules.ProductIdentifierIsS122
            .Evaluate(ds, ValidationContext.Default));
    }

    [Theory]
    [InlineData("S-122")]
    [InlineData("s-122")]
    [InlineData("S-122 Marine Protected Areas")]
    [InlineData("INT.IHO.S-122.0.1.0")] // tolerate trailing fragments? — only checks prefix; this fails.
    public void ProductIdentifierIsS122_PassesOrFails_ByPrefix(string pid)
    {
        var ds = Dataset(productIdentifier: pid);
        var findings = S122MarineProtectedAreaRules.ProductIdentifierIsS122
            .Evaluate(ds, ValidationContext.Default).ToList();

        if (pid.StartsWith("S-122", StringComparison.OrdinalIgnoreCase))
            Assert.Empty(findings);
        else
            Assert.Single(findings);
    }

    [Fact]
    public void ProductIdentifierIsS122_Fails_OnWrongSpec()
    {
        var ds = Dataset(productIdentifier: "S-123");
        var findings = S122MarineProtectedAreaRules.ProductIdentifierIsS122
            .Evaluate(ds, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S122-R-9.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Contains("S-123", f.Message);
    }

    // ── Default ruleset composition + Validate convenience ──────

    [Fact]
    public void Default_ContainsAllSevenRules()
    {
        Assert.Equal(7, S122MarineProtectedAreaRules.Default.Rules.Length);
        var ids = S122MarineProtectedAreaRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S122-R-3.1", ids);
        Assert.Contains("S122-R-4.1", ids);
        Assert.Contains("S122-R-5.1", ids);
        Assert.Contains("S122-R-6.1", ids);
        Assert.Contains("S122-R-6.2", ids);
        Assert.Contains("S122-R-7.1", ids);
        Assert.Contains("S122-R-9.1", ids);
    }

    [Fact]
    public void Validate_OnValidDataset_ProducesNoFindings()
    {
        var ds = Dataset(
            features: new IS122Feature[]
            {
                Mpa("MPA-1", S122GeometryKind.Surface, ClosedSquare(10, 20), scaleMinimum: 50_000),
                Restricted("RA-1", S122GeometryKind.Surface, ClosedSquare(11, 21)),
            },
            infoTypes: new[] { Authority("AUTH-1") },
            productIdentifier: "S-122");

        var report = S122MarineProtectedAreaRules.Validate(ds);
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(7, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidDataset_AggregatesFindingsFromMultipleRules()
    {
        // Surface w/ missing coords (3.1), bad lat (4.1) on a point feature,
        // duplicate ID (6.1), zero scaleMinimum (7.1), wrong product id (9.1).
        var ds = Dataset(
            features: new IS122Feature[]
            {
                Mpa("EMPTY", S122GeometryKind.Surface, ImmutableArray<GeoPosition>.Empty),
                Mpa("BADLL", S122GeometryKind.Point, ImmutableArray.Create(new GeoPosition(95, 0)), scaleMinimum: 0),
                Restricted("DUP", S122GeometryKind.Surface, ClosedSquare(0, 0)),
                Restricted("DUP", S122GeometryKind.Surface, ClosedSquare(1, 1)),
            },
            productIdentifier: "S-201");

        var report = S122MarineProtectedAreaRules.Validate(ds);
        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S122-R-3.1", ids);
        Assert.Contains("S122-R-4.1", ids);
        Assert.Contains("S122-R-6.1", ids);
        Assert.Contains("S122-R-7.1", ids);
        Assert.Contains("S122-R-9.1", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullDataset()
    {
        Assert.Throws<ArgumentNullException>(() => S122MarineProtectedAreaRules.Validate(null!));
    }
}
