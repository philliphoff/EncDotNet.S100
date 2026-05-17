using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S131.Validation;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S131.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S131HarbourInfrastructureRules"/>. Each test
/// constructs a minimal synthetic <see cref="S131HarbourInfrastructureDataset"/>
/// in memory by directly composing typed projection records, bypassing
/// the GML reader and the projection factory. This keeps the tests
/// focused on validation logic and independent of fixture files.
/// </summary>
public class S131HarbourInfrastructureRulesTests
{
    // ── Synthetic-model helpers ──────────────────────────────────────

    private static S131Feature RawFeature(
        string id,
        string featureType,
        ImmutableDictionary<string, string>? attributes = null) => new()
        {
            Id = id,
            FeatureType = featureType,
            Attributes = attributes ?? ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S131ComplexAttribute>.Empty,
            References = ImmutableArray<S131Reference>.Empty,
        };

    private static S131InformationType RawInfoType(string id, string typeCode) => new()
    {
        Id = id,
        TypeCode = typeCode,
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S131ComplexAttribute>.Empty,
    };

    private static S131Geometry PointGeometry(double lat, double lon) => new()
    {
        GeometryType = S131GeometryType.Point,
        Points = ImmutableArray.Create(new GeoPosition(lat, lon)),
    };

    private static S131Geometry CurveGeometry(params (double lat, double lon)[] coords) => new()
    {
        GeometryType = S131GeometryType.Curve,
        Curves = ImmutableArray.Create(
            coords.Select(c => new GeoPosition(c.lat, c.lon)).ToImmutableArray()),
    };

    private static S131Geometry SurfaceGeometry(
        ImmutableArray<GeoPosition> exterior,
        ImmutableArray<ImmutableArray<GeoPosition>>? interior = null) => new()
        {
            GeometryType = S131GeometryType.Surface,
            ExteriorRing = exterior,
            InteriorRings = interior ?? ImmutableArray<ImmutableArray<GeoPosition>>.Empty,
        };

    private static ImmutableArray<GeoPosition> Ring(params (double lat, double lon)[] coords) =>
        coords.Select(c => new GeoPosition(c.lat, c.lon)).ToImmutableArray();

    private static S131HarbourInfrastructure HarbourInfra(
        string id,
        string featureType,
        S131HarbourInfrastructureKind kind,
        S131Geometry? geometry = null,
        ImmutableDictionary<string, string>? attributes = null,
        ImmutableArray<S131ResolvedReference>? resolved = null)
    {
        var raw = RawFeature(id, featureType, attributes);
        return new S131HarbourInfrastructure
        {
            Id = id,
            FeatureType = featureType,
            Kind = kind,
            Geometry = geometry ?? S131Geometry.Empty,
            Source = raw,
            ResolvedReferences = resolved ?? ImmutableArray<S131ResolvedReference>.Empty,
        };
    }

    private static S131LayoutFeature Layout(
        string id,
        string featureType,
        S131LayoutKind kind,
        S131Geometry? geometry = null,
        ImmutableDictionary<string, string>? attributes = null,
        ImmutableArray<S131ResolvedReference>? resolved = null)
    {
        var raw = RawFeature(id, featureType, attributes);
        return new S131LayoutFeature
        {
            Id = id,
            FeatureType = featureType,
            Kind = kind,
            Geometry = geometry ?? S131Geometry.Empty,
            Source = raw,
            ResolvedReferences = resolved ?? ImmutableArray<S131ResolvedReference>.Empty,
        };
    }

    private static S131OtherFeature Other(string id, string featureType) => new()
    {
        Id = id,
        FeatureType = featureType,
        Geometry = PointGeometry(0, 0),
        Source = RawFeature(id, featureType),
    };

    private static S131Authority Authority(
        string id,
        ImmutableArray<S131ResolvedReference>? resolved = null) => new()
        {
            Id = id,
            Source = RawInfoType(id, "Authority"),
            ResolvedReferences = resolved ?? ImmutableArray<S131ResolvedReference>.Empty,
        };

    private static S131OtherInformationType OtherInfo(string id, string typeCode) => new()
    {
        Id = id,
        TypeCode = typeCode,
        Source = RawInfoType(id, typeCode),
    };

    private static S131HarbourInfrastructureDataset Dataset(
        IEnumerable<IS131Feature>? features = null,
        IEnumerable<IS131InformationType>? infoTypes = null)
    {
        var feats = (features ?? Array.Empty<IS131Feature>()).ToImmutableArray();
        var infos = (infoTypes ?? Array.Empty<IS131InformationType>()).ToImmutableArray();

        var rawDataset = new S131Dataset
        {
            ProductIdentifier = "S-131",
            Features = feats.Select(f => f.Source).ToImmutableArray(),
            InformationTypes = infos.Select(i => i.Source).ToImmutableArray(),
        };

        return new S131HarbourInfrastructureDataset
        {
            ProductIdentifier = "S-131",
            Features = feats,
            InformationTypes = infos,
            HarbourInfrastructure = feats.OfType<S131HarbourInfrastructure>().ToImmutableArray(),
            LayoutFeatures = feats.OfType<S131LayoutFeature>().ToImmutableArray(),
            MetadataFeatures = feats.OfType<S131MetadataFeature>().ToImmutableArray(),
            OtherFeatures = feats.OfType<S131OtherFeature>().ToImmutableArray(),
            Authorities = infos.OfType<S131Authority>().ToImmutableArray(),
            ContactDetails = infos.OfType<S131ContactDetails>().ToImmutableArray(),
            RxNInformation = infos.OfType<S131RxNInformation>().ToImmutableArray(),
            Source = rawDataset,
        };
    }

    // ── S131-R-1.1 — harbour-infrastructure geometry present ─────────

    [Fact]
    public void HarbourInfrastructureGeometryPresent_Passes_WhenGeometryNonEmpty()
    {
        var f = HarbourInfra("BOLLARD-1", "Bollard", S131HarbourInfrastructureKind.Bollard,
            PointGeometry(44.6, -63.5));
        var report = S131HarbourInfrastructureRules.HarbourInfrastructureGeometryPresent
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default);
        Assert.Empty(report);
    }

    [Fact]
    public void HarbourInfrastructureGeometryPresent_Fails_WhenEmpty()
    {
        var f = HarbourInfra("BOLLARD-2", "Bollard", S131HarbourInfrastructureKind.Bollard);
        var findings = S131HarbourInfrastructureRules.HarbourInfrastructureGeometryPresent
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("S131-R-1.1", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("BOLLARD-2", finding.RelatedFeatureId);
    }

    [Fact]
    public void HarbourInfrastructureGeometryPresent_Exempts_ContainerKind_HarbourFacility()
    {
        // HarbourFacility may legitimately have no geometry (it
        // aggregates child features via xlinks).
        var f = HarbourInfra("HARBOUR-FAC-1", "HarbourFacility",
            S131HarbourInfrastructureKind.HarbourFacility);
        var findings = S131HarbourInfrastructureRules.HarbourInfrastructureGeometryPresent
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default);
        Assert.Empty(findings);
    }

    // ── S131-R-1.2 — layout-feature geometry present ─────────────────

    [Fact]
    public void LayoutFeatureGeometryPresent_Passes_WhenSurfacePopulated()
    {
        var berth = Layout("BERTH-1", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 1), (1, 1), (0, 0))));
        var findings = S131HarbourInfrastructureRules.LayoutFeatureGeometryPresent
            .Evaluate(Dataset(features: new[] { berth }), ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void LayoutFeatureGeometryPresent_Fails_WhenEmpty()
    {
        var berth = Layout("BERTH-EMPTY", "Berth", S131LayoutKind.Berth);
        var findings = S131HarbourInfrastructureRules.LayoutFeatureGeometryPresent
            .Evaluate(Dataset(features: new[] { berth }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("S131-R-1.2", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("BERTH-EMPTY", finding.RelatedFeatureId);
        Assert.Contains("Berth", finding.Message);
    }

    // ── S131-R-2.1 — availableBerthingLength ─────────────────────────

    [Fact]
    public void AvailableBerthingLengthNonNegative_Passes_WhenAttributeAbsent()
    {
        var berth = Layout("BERTH-A", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 1), (1, 1), (0, 0))));
        var findings = S131HarbourInfrastructureRules.AvailableBerthingLengthNonNegative
            .Evaluate(Dataset(features: new[] { berth }), ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void AvailableBerthingLengthNonNegative_Passes_OnPositiveValue()
    {
        var attrs = ImmutableDictionary<string, string>.Empty
            .Add("availableBerthingLength", "200.5");
        var berth = Layout("BERTH-B", "Berth", S131LayoutKind.Berth, attributes: attrs);
        var findings = S131HarbourInfrastructureRules.AvailableBerthingLengthNonNegative
            .Evaluate(Dataset(features: new[] { berth }), ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void AvailableBerthingLengthNonNegative_Fails_OnNegativeValue()
    {
        var attrs = ImmutableDictionary<string, string>.Empty
            .Add("availableBerthingLength", "-1");
        var berth = Layout("BERTH-NEG", "Berth", S131LayoutKind.Berth, attributes: attrs);
        var findings = S131HarbourInfrastructureRules.AvailableBerthingLengthNonNegative
            .Evaluate(Dataset(features: new[] { berth }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("BERTH-NEG", finding.RelatedFeatureId);
        Assert.Contains("negative", finding.Message);
    }

    [Fact]
    public void AvailableBerthingLengthNonNegative_Fails_OnNonNumericValue()
    {
        var attrs = ImmutableDictionary<string, string>.Empty
            .Add("availableBerthingLength", "two hundred");
        var berth = Layout("BERTH-NAN", "Berth", S131LayoutKind.Berth, attributes: attrs);
        var findings = S131HarbourInfrastructureRules.AvailableBerthingLengthNonNegative
            .Evaluate(Dataset(features: new[] { berth }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("non-numeric", finding.Message);
    }

    // ── S131-R-3.1 — coordinates in WGS-84 range ─────────────────────

    [Fact]
    public void CoordinatesInWgs84Range_Passes_OnValidCoordinates()
    {
        var bollard = HarbourInfra("BO-1", "Bollard", S131HarbourInfrastructureKind.Bollard,
            PointGeometry(44.6, -63.5));
        var line = Layout("FEN-1", "FenderLine", S131LayoutKind.FenderLine,
            CurveGeometry((44.6, -63.5), (44.7, -63.4)));
        var findings = S131HarbourInfrastructureRules.CoordinatesInWgs84Range
            .Evaluate(Dataset(features: new IS131Feature[] { bollard, line }), ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(-90.5, 0.0)]
    [InlineData(0.0, 181.0)]
    [InlineData(0.0, -180.5)]
    public void CoordinatesInWgs84Range_Fails_OnOutOfRange(double lat, double lon)
    {
        var f = HarbourInfra("BAD-COORD", "Bollard", S131HarbourInfrastructureKind.Bollard,
            PointGeometry(lat, lon));
        var findings = S131HarbourInfrastructureRules.CoordinatesInWgs84Range
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("S131-R-3.1", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("BAD-COORD", finding.RelatedFeatureId);
        Assert.NotNull(finding.Point);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Checks_SurfaceRings()
    {
        var bad = Layout("BAD-SURF", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 200), (1, 200), (1, 0), (0, 0))));
        var findings = S131HarbourInfrastructureRules.CoordinatesInWgs84Range
            .Evaluate(Dataset(features: new[] { bad }), ValidationContext.Default).ToList();
        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("BAD-SURF", f.RelatedFeatureId));
    }

    // ── S131-R-3.2 — surface ring closure ────────────────────────────

    [Fact]
    public void SurfaceRingsClosed_Passes_OnClosedRing()
    {
        var f = Layout("OK-SURF", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 1), (1, 1), (0, 0))));
        Assert.Empty(S131HarbourInfrastructureRules.SurfaceRingsClosed
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingsClosed_Fails_WhenRingTooShort()
    {
        var f = Layout("SHORT", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 1), (0, 0))));
        var findings = S131HarbourInfrastructureRules.SurfaceRingsClosed
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("at least 4", finding.Message);
        Assert.Equal("SHORT", finding.RelatedFeatureId);
    }

    [Fact]
    public void SurfaceRingsClosed_Fails_WhenNotClosed()
    {
        var f = Layout("OPEN", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 1), (1, 1), (1, 0))));
        var findings = S131HarbourInfrastructureRules.SurfaceRingsClosed
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("not closed", finding.Message);
        Assert.Equal("OPEN", finding.RelatedFeatureId);
    }

    [Fact]
    public void SurfaceRingsClosed_Checks_InteriorRings()
    {
        var ext = Ring((0, 0), (0, 10), (10, 10), (10, 0), (0, 0));
        var badInterior = Ring((1, 1), (1, 2), (2, 2), (2, 1)); // not closed
        var f = Layout("HOLE", "HarbourBasin", S131LayoutKind.HarbourBasin,
            SurfaceGeometry(ext, ImmutableArray.Create(badInterior)));
        var findings = S131HarbourInfrastructureRules.SurfaceRingsClosed
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("interior ring", finding.Message);
    }

    [Fact]
    public void SurfaceRingsClosed_Ignores_PointAndCurveFeatures()
    {
        var pt = HarbourInfra("P", "Bollard", S131HarbourInfrastructureKind.Bollard,
            PointGeometry(0, 0));
        var cv = Layout("C", "FenderLine", S131LayoutKind.FenderLine,
            CurveGeometry((0, 0), (1, 1)));
        Assert.Empty(S131HarbourInfrastructureRules.SurfaceRingsClosed
            .Evaluate(Dataset(features: new IS131Feature[] { pt, cv }), ValidationContext.Default));
    }

    // ── S131-R-4.1 — unique IDs ──────────────────────────────────────

    [Fact]
    public void UniqueFeatureIds_Passes_WhenAllUnique()
    {
        var a = HarbourInfra("A", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var b = HarbourInfra("B", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        Assert.Empty(S131HarbourInfrastructureRules.UniqueFeatureIds
            .Evaluate(Dataset(features: new[] { a, b }), ValidationContext.Default));
    }

    [Fact]
    public void UniqueFeatureIds_Fails_OnDuplicateFeatureId()
    {
        var a = HarbourInfra("DUP", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var b = HarbourInfra("DUP", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var findings = S131HarbourInfrastructureRules.UniqueFeatureIds
            .Evaluate(Dataset(features: new[] { a, b }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("S131-R-4.1", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("DUP", finding.RelatedFeatureId);
        Assert.Contains("feature", finding.Message);
    }

    [Fact]
    public void UniqueFeatureIds_Fails_OnDuplicateInformationTypeId()
    {
        var a = Authority("AUTH-DUP");
        var b = Authority("AUTH-DUP");
        var findings = S131HarbourInfrastructureRules.UniqueFeatureIds
            .Evaluate(Dataset(infoTypes: new[] { a, b }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("information type", finding.Message);
    }

    // ── S131-R-5.1 — xlink resolution ────────────────────────────────

    [Fact]
    public void ResolvedReferencesNotNull_Passes_WhenAllTargetsResolved()
    {
        var target = HarbourInfra("T", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var resolved = ImmutableArray.Create(new S131ResolvedReference
        {
            Role = "theBollard",
            TargetRef = "T",
            Target = target,
        });
        var src = HarbourInfra("S", "Dolphin", S131HarbourInfrastructureKind.Dolphin,
            PointGeometry(0, 0), resolved: resolved);
        Assert.Empty(S131HarbourInfrastructureRules.ResolvedReferencesNotNull
            .Evaluate(Dataset(features: new[] { src, target }), ValidationContext.Default));
    }

    [Fact]
    public void ResolvedReferencesNotNull_Fails_OnUnresolvedFeatureXlink()
    {
        var unresolved = ImmutableArray.Create(new S131ResolvedReference
        {
            Role = "theBollard",
            TargetRef = "MISSING",
            Target = null,
        });
        var src = HarbourInfra("S", "Dolphin", S131HarbourInfrastructureKind.Dolphin,
            PointGeometry(0, 0), resolved: unresolved);
        var findings = S131HarbourInfrastructureRules.ResolvedReferencesNotNull
            .Evaluate(Dataset(features: new[] { src }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("S131-R-5.1", finding.RuleId);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("S", finding.RelatedFeatureId);
        Assert.Contains("MISSING", finding.Message);
        Assert.Contains("theBollard", finding.Message);
    }

    [Fact]
    public void ResolvedReferencesNotNull_Fails_OnUnresolvedInformationTypeXlink()
    {
        var unresolved = ImmutableArray.Create(new S131ResolvedReference
        {
            Role = "theContactDetails",
            TargetRef = "MISSING-CD",
            Target = null,
        });
        var auth = Authority("AUTH-1", resolved: unresolved);
        var findings = S131HarbourInfrastructureRules.ResolvedReferencesNotNull
            .Evaluate(Dataset(infoTypes: new[] { auth }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("AUTH-1", finding.RelatedFeatureId);
        Assert.Contains("Authority", finding.Message);
    }

    // ── S131-R-6.1 — feature/info-type code recognised ──────────────

    [Fact]
    public void FeatureCodeRecognised_Passes_OnRecognisedKinds()
    {
        var f = HarbourInfra("OK", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var i = Authority("OK-AUTH");
        Assert.Empty(S131HarbourInfrastructureRules.FeatureCodeRecognised
            .Evaluate(Dataset(features: new[] { f }, infoTypes: new[] { i }), ValidationContext.Default));
    }

    [Fact]
    public void FeatureCodeRecognised_Fails_OnOtherFeature()
    {
        var f = Other("MYST", "MysteryFeature");
        var findings = S131HarbourInfrastructureRules.FeatureCodeRecognised
            .Evaluate(Dataset(features: new[] { f }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Equal("S131-R-6.1", finding.RuleId);
        Assert.Equal(ValidationSeverity.Warning, finding.Severity);
        Assert.Equal("MYST", finding.RelatedFeatureId);
        Assert.Contains("MysteryFeature", finding.Message);
    }

    [Fact]
    public void FeatureCodeRecognised_Fails_OnOtherInformationType()
    {
        var i = OtherInfo("WAT", "WatfordCode");
        var findings = S131HarbourInfrastructureRules.FeatureCodeRecognised
            .Evaluate(Dataset(infoTypes: new[] { i }), ValidationContext.Default).ToList();
        var finding = Assert.Single(findings);
        Assert.Contains("WatfordCode", finding.Message);
    }

    // ── Default rule set composition ────────────────────────────────

    [Fact]
    public void Default_ContainsAllEightRules()
    {
        Assert.Equal(8, S131HarbourInfrastructureRules.Default.Rules.Length);
        var ids = S131HarbourInfrastructureRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S131-R-1.1", ids);
        Assert.Contains("S131-R-1.2", ids);
        Assert.Contains("S131-R-2.1", ids);
        Assert.Contains("S131-R-3.1", ids);
        Assert.Contains("S131-R-3.2", ids);
        Assert.Contains("S131-R-4.1", ids);
        Assert.Contains("S131-R-5.1", ids);
        Assert.Contains("S131-R-6.1", ids);
    }

    [Fact]
    public void Validate_OnValidDataset_ProducesNoFindings()
    {
        var bollard = HarbourInfra("BO-1", "Bollard", S131HarbourInfrastructureKind.Bollard,
            PointGeometry(44.6, -63.5));
        var berth = Layout("BE-1", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((44.6, -63.5), (44.6, -63.4), (44.7, -63.4), (44.6, -63.5))));
        var auth = Authority("AU-1");

        var report = S131HarbourInfrastructureRules.Validate(
            Dataset(features: new IS131Feature[] { bollard, berth }, infoTypes: new[] { auth }));
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(8, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidDataset_AggregatesFindingsAcrossRules()
    {
        // Empty-geometry harbour infra (1.1), empty-geometry layout (1.2),
        // negative berthing length (2.1), out-of-range coord (3.1),
        // unknown feature type (6.1), duplicate id (4.1).
        var emptyBollard = HarbourInfra("EMPTY-BO", "Bollard", S131HarbourInfrastructureKind.Bollard);
        var emptyBerth = Layout("EMPTY-BE", "Berth", S131LayoutKind.Berth);
        var negBerth = Layout("NEG-BE", "Berth", S131LayoutKind.Berth,
            SurfaceGeometry(Ring((0, 0), (0, 1), (1, 1), (0, 0))),
            attributes: ImmutableDictionary<string, string>.Empty.Add("availableBerthingLength", "-5"));
        var badCoord = HarbourInfra("BAD", "Bollard", S131HarbourInfrastructureKind.Bollard,
            PointGeometry(99, 0));
        var dup1 = HarbourInfra("DUP", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var dup2 = HarbourInfra("DUP", "Bollard", S131HarbourInfrastructureKind.Bollard, PointGeometry(0, 0));
        var mystery = Other("MYST", "Mystery");

        var report = S131HarbourInfrastructureRules.Validate(
            Dataset(features: new IS131Feature[]
            {
                emptyBollard, emptyBerth, negBerth, badCoord, dup1, dup2, mystery,
            }));

        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S131-R-1.1", ids);
        Assert.Contains("S131-R-1.2", ids);
        Assert.Contains("S131-R-2.1", ids);
        Assert.Contains("S131-R-3.1", ids);
        Assert.Contains("S131-R-4.1", ids);
        Assert.Contains("S131-R-6.1", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullDataset()
    {
        Assert.Throws<ArgumentNullException>(() =>
            S131HarbourInfrastructureRules.Validate(null!));
    }
}
