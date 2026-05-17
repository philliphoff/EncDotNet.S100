using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S127.DataModel;
using EncDotNet.S100.Datasets.S127.Validation;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S127.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S127MarineServicesRules"/>. Each test
/// constructs a minimal synthetic <see cref="S127MarineServicesDataset"/>
/// in memory (no GML fixtures) and asserts the rule fires (or doesn't)
/// with the expected finding shape.
/// </summary>
public class S127MarineServicesRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static S127Feature SourceFeature(
        string id,
        string featureType,
        GmlGeometryType geometryType = GmlGeometryType.None,
        IDictionary<string, string>? attributes = null,
        IEnumerable<S127ComplexAttribute>? complex = null)
        => new()
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = geometryType,
            Attributes = attributes is null
                ? ImmutableDictionary<string, string>.Empty
                : attributes.ToImmutableDictionary(),
            ComplexAttributes = complex is null
                ? ImmutableArray<S127ComplexAttribute>.Empty
                : complex.ToImmutableArray(),
            Points = ImmutableArray<(double, double)>.Empty,
            Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            ExteriorRing = ImmutableArray<(double, double)>.Empty,
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
        };

    private static S127PilotBoardingPlace Pbp(
        string id,
        S127GeometryKind kind,
        params (double lat, double lon)[] coords)
        => new()
        {
            Id = id,
            GeometryKind = kind,
            Coordinates = coords.Select(c => new GeoPosition(c.lat, c.lon)).ToImmutableArray(),
            Source = SourceFeature(id, "PilotBoardingPlace"),
        };

    private static S127RegulatedArea Area(
        string id,
        S127GeometryKind kind,
        params (double lat, double lon)[] coords)
        => new()
        {
            Id = id,
            FeatureType = "RestrictedArea",
            Kind = S127RegulatedAreaKind.RestrictedArea,
            GeometryKind = kind,
            Coordinates = coords.Select(c => new GeoPosition(c.lat, c.lon)).ToImmutableArray(),
            Source = SourceFeature(id, "RestrictedArea"),
        };

    private static S127RouteingMeasure Curve(
        string id,
        params (double lat, double lon)[] coords)
        => new()
        {
            Id = id,
            GeometryKind = S127GeometryKind.Curve,
            Coordinates = coords.Select(c => new GeoPosition(c.lat, c.lon)).ToImmutableArray(),
            Source = SourceFeature(id, "RouteingMeasure"),
        };

    private static S127VesselTrafficServiceArea VtsArea(
        string id,
        IDictionary<string, string>? attrs = null,
        IEnumerable<S127ComplexAttribute>? complex = null,
        params (double lat, double lon)[] coords)
        => new()
        {
            Id = id,
            GeometryKind = coords.Length > 0 ? S127GeometryKind.Surface : S127GeometryKind.None,
            Coordinates = coords.Select(c => new GeoPosition(c.lat, c.lon)).ToImmutableArray(),
            Source = SourceFeature(
                id, "VesselTrafficServiceArea",
                geometryType: coords.Length > 0 ? GmlGeometryType.Surface : GmlGeometryType.None,
                attributes: attrs,
                complex: complex),
        };

    private static S127Authority Authority(string id, string? name = null)
        => new()
        {
            Id = id,
            GeometryKind = S127GeometryKind.None,
            AuthorityName = name,
            Source = SourceFeature(
                id, "Authority",
                attributes: name is null
                    ? null
                    : new Dictionary<string, string> { ["authorityName"] = name }),
        };

    private static S127MarineServicesDataset Dataset(params IS127Feature[] features)
    {
        var array = features.ToImmutableArray();
        var source = new S127Dataset
        {
            DatasetIdentifier = "TEST",
            ProductIdentifier = "S-127",
            Features = ImmutableArray<S127Feature>.Empty,
            InformationTypes = ImmutableArray<S127InformationType>.Empty,
        };
        return new S127MarineServicesDataset
        {
            DatasetIdentifier = "TEST",
            ProductIdentifier = "S-127",
            Features = array,
            Authorities = array.OfType<S127Authority>().ToImmutableArray(),
            OtherFeatures = array.OfType<S127OtherFeature>().ToImmutableArray(),
            Source = source,
        };
    }

    // ── S127-R-12.1 — WGS-84 lat/lon in range ─────────────────────

    [Fact]
    public void WgsLatLonInRange_Passes_OnValidCoordinates()
    {
        var ds = Dataset(Pbp("P1", S127GeometryKind.Point, (10, 20)));
        Assert.Empty(S127MarineServicesRules.WgsLatLonInRange.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void WgsLatLonInRange_Passes_OnRangeBoundaries()
    {
        var ds = Dataset(
            Pbp("MIN", S127GeometryKind.Point, (-90, -180)),
            Pbp("MAX", S127GeometryKind.Point, (90, 180)));
        Assert.Empty(S127MarineServicesRules.WgsLatLonInRange.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void WgsLatLonInRange_Passes_OnAuthorityWithoutGeometry()
    {
        // Container-feature tolerance: Authority carries no coordinates,
        // so the lat/lon rule trivially passes.
        var ds = Dataset(Authority("AUTH-1", "Port Authority"));
        Assert.Empty(S127MarineServicesRules.WgsLatLonInRange.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void WgsLatLonInRange_Fails_OnOutOfRangeLatitude()
    {
        var ds = Dataset(Pbp("BAD", S127GeometryKind.Point, (95, 0)));
        var f = Assert.Single(
            S127MarineServicesRules.WgsLatLonInRange.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S127-R-12.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Equal("TEST", f.DatasetId);
        Assert.Contains("latitude", f.Message);
    }

    [Fact]
    public void WgsLatLonInRange_Fails_OnOutOfRangeLongitude()
    {
        var ds = Dataset(Pbp("BAD", S127GeometryKind.Point, (0, 181)));
        var f = Assert.Single(
            S127MarineServicesRules.WgsLatLonInRange.Evaluate(ds, ValidationContext.Default));
        Assert.Contains("longitude", f.Message);
    }

    [Fact]
    public void WgsLatLonInRange_EmitsOneFindingPerOffendingCoordinate()
    {
        var ds = Dataset(Curve("LINE", (0, 0), (0, 181), (95, 0)));
        var findings = S127MarineServicesRules.WgsLatLonInRange
            .Evaluate(ds, ValidationContext.Default).ToList();
        Assert.Equal(2, findings.Count);
    }

    // ── S127-R-12.2 — PilotBoardingPlace requires geometry ─────────

    [Fact]
    public void PilotBoardingPlaceHasGeometry_Passes_OnPoint()
    {
        var ds = Dataset(Pbp("P1", S127GeometryKind.Point, (10, 20)));
        Assert.Empty(S127MarineServicesRules.PilotBoardingPlaceHasGeometry
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void PilotBoardingPlaceHasGeometry_Passes_OnSurface()
    {
        var ds = Dataset(Pbp("P1", S127GeometryKind.Surface,
            (0, 0), (0, 1), (1, 1), (0, 0)));
        Assert.Empty(S127MarineServicesRules.PilotBoardingPlaceHasGeometry
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void PilotBoardingPlaceHasGeometry_Fails_OnEmptyCoordinates()
    {
        var ds = Dataset(Pbp("NO_GEOM", S127GeometryKind.None));
        var f = Assert.Single(S127MarineServicesRules.PilotBoardingPlaceHasGeometry
            .Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S127-R-12.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("NO_GEOM", f.RelatedFeatureId);
    }

    [Fact]
    public void PilotBoardingPlaceHasGeometry_IgnoresOtherFeatureTypes()
    {
        // Authorities have no geometry but are NOT in scope of this rule.
        var ds = Dataset(Authority("AUTH-1", "Pilot Service"));
        Assert.Empty(S127MarineServicesRules.PilotBoardingPlaceHasGeometry
            .Evaluate(ds, ValidationContext.Default));
    }

    // ── S127-R-12.3 — Surface polygon closure ──────────────────────

    [Fact]
    public void SurfacePolygonClosure_Passes_OnClosedRing()
    {
        var ds = Dataset(Area("A1", S127GeometryKind.Surface,
            (0, 0), (0, 1), (1, 1), (0, 0)));
        Assert.Empty(S127MarineServicesRules.SurfacePolygonClosure
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void SurfacePolygonClosure_Passes_OnFloatingPointDrift()
    {
        var ds = Dataset(Area("A1", S127GeometryKind.Surface,
            (0, 0), (0, 1), (1, 1), (1e-12, 1e-12)));
        Assert.Empty(S127MarineServicesRules.SurfacePolygonClosure
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void SurfacePolygonClosure_Fails_OnTooFewVertices()
    {
        var ds = Dataset(Area("A1", S127GeometryKind.Surface,
            (0, 0), (0, 1), (0, 0)));
        var f = Assert.Single(S127MarineServicesRules.SurfacePolygonClosure
            .Evaluate(ds, ValidationContext.Default));
        Assert.Contains("only 3 vertex", f.Message);
    }

    [Fact]
    public void SurfacePolygonClosure_Fails_OnUnclosedRing()
    {
        var ds = Dataset(Area("A1", S127GeometryKind.Surface,
            (0, 0), (0, 1), (1, 1), (1, 0)));
        var f = Assert.Single(S127MarineServicesRules.SurfacePolygonClosure
            .Evaluate(ds, ValidationContext.Default));
        Assert.Contains("unclosed", f.Message);
    }

    [Fact]
    public void SurfacePolygonClosure_IgnoresNonSurfaceGeometries()
    {
        var ds = Dataset(
            Pbp("P1", S127GeometryKind.Point, (0, 0)),
            Curve("L1", (0, 0), (0, 1)));
        Assert.Empty(S127MarineServicesRules.SurfacePolygonClosure
            .Evaluate(ds, ValidationContext.Default));
    }

    // ── S127-R-12.4 — Curve minimum vertices ───────────────────────

    [Fact]
    public void CurveMinimumVertices_Passes_OnTwoOrMore()
    {
        var ds = Dataset(Curve("L1", (0, 0), (0, 1)));
        Assert.Empty(S127MarineServicesRules.CurveMinimumVertices
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void CurveMinimumVertices_Fails_OnSingleVertex()
    {
        var ds = Dataset(Curve("L1", (0, 0)));
        var f = Assert.Single(S127MarineServicesRules.CurveMinimumVertices
            .Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S127-R-12.4", f.RuleId);
        Assert.Equal("L1", f.RelatedFeatureId);
    }

    [Fact]
    public void CurveMinimumVertices_IgnoresPointAndSurface()
    {
        var ds = Dataset(
            Pbp("P1", S127GeometryKind.Point, (0, 0)),
            Area("A1", S127GeometryKind.Surface, (0, 0), (0, 1), (1, 1), (0, 0)));
        Assert.Empty(S127MarineServicesRules.CurveMinimumVertices
            .Evaluate(ds, ValidationContext.Default));
    }

    // ── S127-R-12.5 — Vessel size limits monotonic ────────────────

    [Fact]
    public void VesselSizeLimitsMonotonic_Passes_WhenAttributesAbsent()
    {
        var ds = Dataset(VtsArea("V1"));
        Assert.Empty(S127MarineServicesRules.VesselSizeLimitsMonotonic
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void VesselSizeLimitsMonotonic_Passes_OnSaneEnvelope()
    {
        var ds = Dataset(VtsArea("V1",
            attrs: new Dictionary<string, string>
            {
                ["minimumVesselsLength"] = "50",
                ["maximumVesselsLength"] = "300",
                ["minimumVesselsDraught"] = "2.5",
                ["maximumVesselsDraught"] = "12",
            }));
        Assert.Empty(S127MarineServicesRules.VesselSizeLimitsMonotonic
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void VesselSizeLimitsMonotonic_Fails_WhenLengthInverted()
    {
        var ds = Dataset(VtsArea("V1",
            attrs: new Dictionary<string, string>
            {
                ["minimumVesselsLength"] = "400",
                ["maximumVesselsLength"] = "300",
            }));
        var f = Assert.Single(S127MarineServicesRules.VesselSizeLimitsMonotonic
            .Evaluate(ds, ValidationContext.Default));
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Contains("minimumVesselsLength=400", f.Message);
    }

    [Fact]
    public void VesselSizeLimitsMonotonic_Fails_OnMultiplePairs()
    {
        var ds = Dataset(VtsArea("V1",
            attrs: new Dictionary<string, string>
            {
                ["minimumVesselsLength"] = "400",
                ["maximumVesselsLength"] = "300",
                ["minimumVesselsBeam"] = "60",
                ["maximumVesselsBeam"] = "40",
            }));
        Assert.Equal(2, S127MarineServicesRules.VesselSizeLimitsMonotonic
            .Evaluate(ds, ValidationContext.Default).Count());
    }

    [Fact]
    public void VesselSizeLimitsMonotonic_IgnoresUnparseableValues()
    {
        var ds = Dataset(VtsArea("V1",
            attrs: new Dictionary<string, string>
            {
                ["minimumVesselsLength"] = "not-a-number",
                ["maximumVesselsLength"] = "300",
            }));
        Assert.Empty(S127MarineServicesRules.VesselSizeLimitsMonotonic
            .Evaluate(ds, ValidationContext.Default));
    }

    // ── S127-R-12.6 — Service hours validity ───────────────────────

    private static S127ComplexAttribute Availability(IDictionary<string, string> subs)
        => new()
        {
            Code = "availability",
            SubAttributes = subs.ToImmutableDictionary(),
        };

    [Fact]
    public void ServiceHoursValidity_Passes_OnNormalTimeRange()
    {
        var ds = Dataset(VtsArea("V1", complex: new[]
        {
            Availability(new Dictionary<string, string>
            {
                ["timeOfDayStart"] = "08:00",
                ["timeOfDayEnd"] = "18:00",
            }),
        }));
        Assert.Empty(S127MarineServicesRules.ServiceHoursValidity
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void ServiceHoursValidity_Fails_OnInvertedTimeRange()
    {
        var ds = Dataset(VtsArea("V1", complex: new[]
        {
            Availability(new Dictionary<string, string>
            {
                ["timeOfDayStart"] = "18:00",
                ["timeOfDayEnd"] = "08:00",
            }),
        }));
        var f = Assert.Single(S127MarineServicesRules.ServiceHoursValidity
            .Evaluate(ds, ValidationContext.Default));
        Assert.Contains("timeOfDayStart=18:00", f.Message);
    }

    [Fact]
    public void ServiceHoursValidity_Passes_OnNormalDateRange()
    {
        var ds = Dataset(VtsArea("V1", complex: new[]
        {
            Availability(new Dictionary<string, string>
            {
                ["dateStart"] = "2025-01-01",
                ["dateEnd"] = "2025-12-31",
            }),
        }));
        Assert.Empty(S127MarineServicesRules.ServiceHoursValidity
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void ServiceHoursValidity_Fails_OnInvertedDateRange()
    {
        var ds = Dataset(VtsArea("V1", complex: new[]
        {
            Availability(new Dictionary<string, string>
            {
                ["dateStart"] = "2025-12-31",
                ["dateEnd"] = "2025-01-01",
            }),
        }));
        var f = Assert.Single(S127MarineServicesRules.ServiceHoursValidity
            .Evaluate(ds, ValidationContext.Default));
        Assert.Contains("dateStart=2025-12-31", f.Message);
    }

    [Fact]
    public void ServiceHoursValidity_IgnoresUnparseableValues()
    {
        var ds = Dataset(VtsArea("V1", complex: new[]
        {
            Availability(new Dictionary<string, string>
            {
                ["timeOfDayStart"] = "not-a-time",
                ["timeOfDayEnd"] = "08:00",
            }),
        }));
        Assert.Empty(S127MarineServicesRules.ServiceHoursValidity
            .Evaluate(ds, ValidationContext.Default));
    }

    // ── S127-R-12.7 — Unique feature IDs ───────────────────────────

    [Fact]
    public void UniqueFeatureIds_Passes_WhenAllDistinct()
    {
        var ds = Dataset(
            Pbp("A", S127GeometryKind.Point, (0, 0)),
            Pbp("B", S127GeometryKind.Point, (1, 1)));
        Assert.Empty(S127MarineServicesRules.UniqueFeatureIds
            .Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void UniqueFeatureIds_Fails_OnDuplicate()
    {
        var ds = Dataset(
            Pbp("DUP", S127GeometryKind.Point, (0, 0)),
            Pbp("DUP", S127GeometryKind.Point, (1, 1)));
        var f = Assert.Single(S127MarineServicesRules.UniqueFeatureIds
            .Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S127-R-12.7", f.RuleId);
        Assert.Equal("DUP", f.RelatedFeatureId);
        Assert.Contains("2 features", f.Message);
    }

    [Fact]
    public void UniqueFeatureIds_TreatsIdsCaseInsensitive()
    {
        var ds = Dataset(
            Pbp("Dup", S127GeometryKind.Point, (0, 0)),
            Pbp("DUP", S127GeometryKind.Point, (1, 1)));
        var f = Assert.Single(S127MarineServicesRules.UniqueFeatureIds
            .Evaluate(ds, ValidationContext.Default));
        Assert.Equal(ValidationSeverity.Error, f.Severity);
    }

    // ── S127-R-12.8 — Authority name present ───────────────────────

    [Fact]
    public void AuthorityNamePresent_Passes_WhenNameSupplied()
    {
        var ds = Dataset(Authority("A1", "Port Authority of New York and New Jersey"));
        Assert.Empty(S127MarineServicesRules.AuthorityNamePresent
            .Evaluate(ds, ValidationContext.Default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AuthorityNamePresent_Fails_OnMissingOrBlankName(string? name)
    {
        var ds = Dataset(Authority("A1", name));
        var f = Assert.Single(S127MarineServicesRules.AuthorityNamePresent
            .Evaluate(ds, ValidationContext.Default));
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("A1", f.RelatedFeatureId);
    }

    // ── Default ruleset composition ───────────────────────────────

    [Fact]
    public void Default_ContainsAllEightRules()
    {
        Assert.Equal(8, S127MarineServicesRules.Default.Rules.Length);
        var ids = S127MarineServicesRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S127-R-12.1", ids);
        Assert.Contains("S127-R-12.2", ids);
        Assert.Contains("S127-R-12.3", ids);
        Assert.Contains("S127-R-12.4", ids);
        Assert.Contains("S127-R-12.5", ids);
        Assert.Contains("S127-R-12.6", ids);
        Assert.Contains("S127-R-12.7", ids);
        Assert.Contains("S127-R-12.8", ids);
    }

    [Fact]
    public void Validate_OnValidDataset_ProducesNoFindings()
    {
        var ds = Dataset(
            Pbp("P1", S127GeometryKind.Point, (40.5, -74.0)),
            Area("RA1", S127GeometryKind.Surface,
                (40, -75), (40, -74), (41, -74), (40, -75)),
            Curve("L1", (40, -74), (41, -73)),
            Authority("AUTH-1", "NY Harbor"));
        var report = S127MarineServicesRules.Validate(ds);
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(8, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidDataset_AggregatesFindingsFromMultipleRules()
    {
        // Out-of-range lat (12.1 fires), missing PBP geometry (12.2 fires),
        // unclosed surface (12.3 fires), duplicate id (12.7 fires),
        // authority without name (12.8 fires).
        var ds = Dataset(
            Pbp("BAD-LAT", S127GeometryKind.Point, (95, 0)),
            Pbp("NO-GEOM", S127GeometryKind.None),
            Area("UNCLOSED", S127GeometryKind.Surface,
                (0, 0), (0, 1), (1, 1), (1, 0)),
            Pbp("DUP", S127GeometryKind.Point, (0, 0)),
            Pbp("DUP", S127GeometryKind.Point, (1, 1)),
            Authority("A1"));

        var report = S127MarineServicesRules.Validate(ds);
        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S127-R-12.1", ids);
        Assert.Contains("S127-R-12.2", ids);
        Assert.Contains("S127-R-12.3", ids);
        Assert.Contains("S127-R-12.7", ids);
        Assert.Contains("S127-R-12.8", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullDataset()
    {
        Assert.Throws<ArgumentNullException>(() => S127MarineServicesRules.Validate(null!));
    }
}
