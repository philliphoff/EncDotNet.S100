using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Datasets.S201.DataModel;
using EncDotNet.S100.Datasets.S201.Validation;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S201.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S201AtonRules"/>. Each test constructs a
/// minimal synthetic <see cref="S201AtonInventory"/> in memory (no GML
/// fixtures) and asserts the rule fires (or doesn't) with the expected
/// finding shape.
/// </summary>
public class S201AtonRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private const string DatasetId = "S201-DS-1";

    private static S201Feature SourceFeature(
        string id,
        string featureType = "LateralBuoy",
        params S201FeatureReference[] featureReferences) => new()
    {
        Id = id,
        FeatureType = featureType,
        GeometryType = GmlGeometryType.Point,
        Points = ImmutableArray<(double, double)>.Empty,
        Curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
        ExteriorRing = ImmutableArray<(double, double)>.Empty,
        InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S201ComplexAttribute>.Empty,
        InformationReferences = ImmutableArray<S201InformationReference>.Empty,
        FeatureReferences = featureReferences.ToImmutableArray(),
    };

    private static S201StructureObject Structure(
        string id,
        string featureClass = "LateralBuoy",
        double? lat = 0,
        double? lon = 0,
        S201DateRange? fixedRange = null,
        S201DateRange? periodicRange = null)
    {
        var coords = (lat is null || lon is null)
            ? ImmutableArray<GeoPosition>.Empty
            : ImmutableArray.Create(new GeoPosition(lat.Value, lon.Value));
        return new S201StructureObject
        {
            Id = id,
            FeatureClass = featureClass,
            Source = SourceFeature(id, featureClass),
            GeometryKind = coords.IsEmpty ? S201GeometryKind.None : S201GeometryKind.Point,
            Coordinates = coords,
            FixedDateRange = fixedRange,
            PeriodicDateRange = periodicRange,
        };
    }

    private static S201Equipment Equipment(
        string id,
        string featureClass = "FogSignal",
        double? lat = 0,
        double? lon = 0,
        S201StructureObject? host = null)
    {
        var coords = (lat is null || lon is null)
            ? ImmutableArray<GeoPosition>.Empty
            : ImmutableArray.Create(new GeoPosition(lat.Value, lon.Value));
        var eq = new S201Equipment
        {
            Id = id,
            FeatureClass = featureClass,
            Source = SourceFeature(id, featureClass),
            GeometryKind = coords.IsEmpty ? S201GeometryKind.None : S201GeometryKind.Point,
            Coordinates = coords,
        };
        if (host is not null)
            SetHostStructure(eq, host);
        return eq;
    }

    private static readonly System.Reflection.PropertyInfo HostStructureProperty =
        typeof(S201Equipment).GetProperty(nameof(S201Equipment.HostStructure))!;

    private static void SetHostStructure(S201Equipment equipment, S201StructureObject host)
    {
        // HostStructure has an internal setter (resolved by the typed projection);
        // tests live outside the S-201 assembly and use reflection to set it.
        HostStructureProperty.SetValue(equipment, host);
    }

    private static S201ElectronicAtoN Ais(
        string id,
        AisAtonKind kind = AisAtonKind.Virtual,
        string? mmsi = "992341001",
        double? lat = 0,
        double? lon = 0)
    {
        var featureClass = kind switch
        {
            AisAtonKind.Physical => "PhysicalAISAidToNavigation",
            AisAtonKind.Synthetic => "SyntheticAISAidToNavigation",
            AisAtonKind.Virtual => "VirtualAISAidToNavigation",
            _ => "VirtualAISAidToNavigation",
        };
        var coords = (lat is null || lon is null)
            ? ImmutableArray<GeoPosition>.Empty
            : ImmutableArray.Create(new GeoPosition(lat.Value, lon.Value));
        return new S201ElectronicAtoN
        {
            Id = id,
            FeatureClass = featureClass,
            Source = SourceFeature(id, featureClass),
            Kind = kind,
            MmsiCode = mmsi,
            GeometryKind = coords.IsEmpty ? S201GeometryKind.None : S201GeometryKind.Point,
            Coordinates = coords,
        };
    }

    private static S201AtonStatusInformation StatusInfo(string id, int? changeTypes = 1) => new()
    {
        Id = id,
        ChangeTypes = changeTypes,
    };

    private static S201AtonAggregation Aggregation(string id, params S201AtonObject[] peers) => new()
    {
        Id = id,
        Peers = peers.ToImmutableArray(),
    };

    private static S201AtonAssociation Association(string id, params S201AtonObject[] peers) => new()
    {
        Id = id,
        Peers = peers.ToImmutableArray(),
    };

    /// <summary>
    /// Adds a synthetic source feature for an aggregation/association with
    /// <paramref name="peerCount"/> <c>peer</c>-role references — used to
    /// drive the unresolved-xlink check.
    /// </summary>
    private static S201Feature AggregationSource(string id, string featureType, int peerCount)
    {
        var refs = Enumerable.Range(0, peerCount)
            .Select(i => new S201FeatureReference { Role = "peer", TargetRef = $"target-{id}-{i}" })
            .ToArray();
        return SourceFeature(id, featureType, refs);
    }

    private static S201AtonInventory Inventory(
        ImmutableArray<S201AtonObject>? atons = null,
        ImmutableArray<S201AtonStatusInformation>? statusInfo = null,
        ImmutableArray<S201AtonAggregation>? aggregations = null,
        ImmutableArray<S201AtonAssociation>? associations = null,
        ImmutableArray<S201Feature>? sourceFeatures = null)
    {
        var atonArray = atons ?? ImmutableArray<S201AtonObject>.Empty;
        var structures = atonArray.OfType<S201StructureObject>().ToImmutableArray();
        var equipment = atonArray.OfType<S201Equipment>().ToImmutableArray();
        var electronic = atonArray.OfType<S201ElectronicAtoN>().ToImmutableArray();

        var sourceFeatureArray = sourceFeatures
            ?? atonArray.Select(a => a.Source).ToImmutableArray();

        var source = new S201Dataset
        {
            DatasetIdentifier = DatasetId,
            ProductIdentifier = "S-201",
            Features = sourceFeatureArray,
            InformationTypes = ImmutableArray<S201InformationType>.Empty,
        };

        return new S201AtonInventory
        {
            DatasetIdentifier = DatasetId,
            ProductIdentifier = "S-201",
            AtoNs = atonArray,
            Structures = structures,
            Equipment = equipment,
            ElectronicAtoNs = electronic,
            Aggregations = aggregations ?? ImmutableArray<S201AtonAggregation>.Empty,
            Associations = associations ?? ImmutableArray<S201AtonAssociation>.Empty,
            StatusInformation = statusInfo ?? ImmutableArray<S201AtonStatusInformation>.Empty,
            Source = source,
        };
    }

    // ── S201-R-1.1 — coordinates in WGS-84 range ─────────────────

    [Fact]
    public void CoordinatesInWgs84Range_Passes_OnValidPositions()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", lat: -89.9, lon: -179.9),
            Structure("S2", lat: 89.9, lon: 179.9)));
        Assert.Empty(S201AtonRules.CoordinatesInWgs84Range.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesInWgs84Range_IgnoresGeometrylessAtoNs()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", lat: null, lon: null)));
        Assert.Empty(S201AtonRules.CoordinatesInWgs84Range.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnOutOfRangeLatitude()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("BAD", lat: 95.0, lon: 0)));
        var f = Assert.Single(S201AtonRules.CoordinatesInWgs84Range.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-1.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Equal(DatasetId, f.DatasetId);
        Assert.Contains("latitude", f.Message);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnOutOfRangeLongitude()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("BAD", lat: 0, lon: 181.0)));
        var f = Assert.Single(S201AtonRules.CoordinatesInWgs84Range.Evaluate(inv, ValidationContext.Default));
        Assert.Contains("longitude", f.Message);
    }

    // ── S201-R-1.2 — gml:id uniqueness ───────────────────────────

    [Fact]
    public void GmlIdsUnique_Passes_OnDistinctIds()
    {
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(Structure("S1"), Structure("S2")),
            statusInfo: ImmutableArray.Create(StatusInfo("I1")));
        Assert.Empty(S201AtonRules.GmlIdsUnique.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void GmlIdsUnique_Fails_OnDuplicateAtonIds()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1"), Structure("S1"), Structure("S2")));
        var f = Assert.Single(S201AtonRules.GmlIdsUnique.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-1.2", f.RuleId);
        Assert.Equal("S1", f.RelatedFeatureId);
    }

    [Fact]
    public void GmlIdsUnique_Fails_AcrossAtonAndInformationType()
    {
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(Structure("X1")),
            statusInfo: ImmutableArray.Create(StatusInfo("X1")));
        var f = Assert.Single(S201AtonRules.GmlIdsUnique.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("X1", f.RelatedFeatureId);
    }

    [Fact]
    public void GmlIdsUnique_CaseInsensitive()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("Aid-1"), Structure("aid-1")));
        Assert.Single(S201AtonRules.GmlIdsUnique.Evaluate(inv, ValidationContext.Default));
    }

    // ── S201-R-1.3 — navigable AtoN has geometry ─────────────────

    [Fact]
    public void NavigableAtoNHasGeometry_Passes_WhenStructureHasGeometry()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", lat: 10, lon: 20)));
        Assert.Empty(S201AtonRules.NavigableAtoNHasGeometry.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void NavigableAtoNHasGeometry_Fails_WhenStructureHasNoGeometry()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S-NOPOS", lat: null, lon: null)));
        var f = Assert.Single(S201AtonRules.NavigableAtoNHasGeometry.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-1.3", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("S-NOPOS", f.RelatedFeatureId);
    }

    [Fact]
    public void NavigableAtoNHasGeometry_IgnoresVirtualAis()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("V1", AisAtonKind.Virtual, lat: null, lon: null)));
        Assert.Empty(S201AtonRules.NavigableAtoNHasGeometry.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void NavigableAtoNHasGeometry_FiresForPhysicalAisWithoutPosition()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("P1", AisAtonKind.Physical, lat: null, lon: null)));
        Assert.Single(S201AtonRules.NavigableAtoNHasGeometry.Evaluate(inv, ValidationContext.Default));
    }

    // ── S201-R-2.1 — physical / synthetic AIS MMSI ───────────────

    [Theory]
    [InlineData(AisAtonKind.Physical)]
    [InlineData(AisAtonKind.Synthetic)]
    public void PhysicalAisHasMmsi_Passes_OnNineDigitMmsi(AisAtonKind kind)
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("A1", kind, "992341001")));
        Assert.Empty(S201AtonRules.PhysicalAisHasMmsi.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void PhysicalAisHasMmsi_IgnoresVirtualAis()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("V1", AisAtonKind.Virtual, mmsi: null)));
        Assert.Empty(S201AtonRules.PhysicalAisHasMmsi.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void PhysicalAisHasMmsi_IgnoresNonAis()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(Structure("S1")));
        Assert.Empty(S201AtonRules.PhysicalAisHasMmsi.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void PhysicalAisHasMmsi_Fails_OnMissingMmsi()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("P1", AisAtonKind.Physical, mmsi: null)));
        var f = Assert.Single(S201AtonRules.PhysicalAisHasMmsi.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-2.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Contains("missing", f.Message);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678X")]
    public void PhysicalAisHasMmsi_Fails_OnMalformedMmsi(string mmsi)
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("P1", AisAtonKind.Physical, mmsi)));
        var f = Assert.Single(S201AtonRules.PhysicalAisHasMmsi.Evaluate(inv, ValidationContext.Default));
        Assert.Contains("malformed", f.Message);
    }

    // ── S201-R-2.2 — virtual AIS MMSI format ─────────────────────

    [Fact]
    public void VirtualAisMmsiFormat_PassesWhenMissing()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("V1", AisAtonKind.Virtual, mmsi: null)));
        Assert.Empty(S201AtonRules.VirtualAisMmsiFormat.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void VirtualAisMmsiFormat_PassesOnNineDigitMmsi()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("V1", AisAtonKind.Virtual, "992341001")));
        Assert.Empty(S201AtonRules.VirtualAisMmsiFormat.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void VirtualAisMmsiFormat_FailsOnMalformedMmsi()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Ais("V1", AisAtonKind.Virtual, "12345")));
        var f = Assert.Single(S201AtonRules.VirtualAisMmsiFormat.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-2.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
    }

    // ── S201-R-3.1 — ChangeTypes codelist ────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ChangeTypesInEnumeration_PassesOnValidCodes(int code)
    {
        var inv = Inventory(statusInfo: ImmutableArray.Create(StatusInfo("I1", code)));
        Assert.Empty(S201AtonRules.ChangeTypesInEnumeration.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void ChangeTypesInEnumeration_IgnoresNullCode()
    {
        var inv = Inventory(statusInfo: ImmutableArray.Create(StatusInfo("I1", null)));
        Assert.Empty(S201AtonRules.ChangeTypesInEnumeration.Evaluate(inv, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(99)]
    public void ChangeTypesInEnumeration_FailsOnOutOfRangeCode(int code)
    {
        var inv = Inventory(statusInfo: ImmutableArray.Create(StatusInfo("I1", code)));
        var f = Assert.Single(S201AtonRules.ChangeTypesInEnumeration.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-3.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("I1", f.RelatedFeatureId);
    }

    // ── S201-R-4.1 — date range ordering ─────────────────────────

    [Fact]
    public void DateRangeOrdered_PassesOnOrderedFixedRange()
    {
        var range = new S201DateRange
        {
            Start = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            End = DateTimeOffset.Parse("2025-12-31T00:00:00Z"),
        };
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", fixedRange: range)));
        Assert.Empty(S201AtonRules.DateRangeOrdered.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void DateRangeOrdered_IgnoresPartialRange()
    {
        var range = new S201DateRange { Start = DateTimeOffset.Parse("2025-01-01T00:00:00Z") };
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", fixedRange: range)));
        Assert.Empty(S201AtonRules.DateRangeOrdered.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void DateRangeOrdered_FailsOnInvertedFixedRange()
    {
        var range = new S201DateRange
        {
            Start = DateTimeOffset.Parse("2025-12-31T00:00:00Z"),
            End = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
        };
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", fixedRange: range)));
        var f = Assert.Single(S201AtonRules.DateRangeOrdered.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-4.1", f.RuleId);
        Assert.Contains("fixedDateRange", f.Message);
    }

    [Fact]
    public void DateRangeOrdered_FailsOnInvertedPeriodicRange()
    {
        var range = new S201DateRange
        {
            Start = DateTimeOffset.Parse("2025-08-01T00:00:00Z"),
            End = DateTimeOffset.Parse("2025-06-01T00:00:00Z"),
        };
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Structure("S1", periodicRange: range)));
        var f = Assert.Single(S201AtonRules.DateRangeOrdered.Evaluate(inv, ValidationContext.Default));
        Assert.Contains("periodicDateRange", f.Message);
    }

    // ── S201-R-5.1 — equipment has host structure ────────────────

    [Fact]
    public void EquipmentHasHostStructure_PassesWhenHostResolved()
    {
        var host = Structure("S1");
        var eq = Equipment("E1", host: host);
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(host, eq));
        Assert.Empty(S201AtonRules.EquipmentHasHostStructure.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void EquipmentHasHostStructure_FailsWhenHostMissing()
    {
        var inv = Inventory(atons: ImmutableArray.Create<S201AtonObject>(
            Equipment("E1", host: null)));
        var f = Assert.Single(S201AtonRules.EquipmentHasHostStructure.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-5.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("E1", f.RelatedFeatureId);
    }

    // ── S201-R-6.1 — aggregation has members ─────────────────────

    [Fact]
    public void AggregationHasMembers_PassesOnTwoPeers()
    {
        var a = Structure("A");
        var b = Structure("B");
        var agg = Aggregation("AGG1", a, b);
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(a, b),
            aggregations: ImmutableArray.Create(agg));
        Assert.Empty(S201AtonRules.AggregationHasMembers.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void AggregationHasMembers_FailsOnEmpty()
    {
        var inv = Inventory(aggregations: ImmutableArray.Create(Aggregation("AGG1")));
        var f = Assert.Single(S201AtonRules.AggregationHasMembers.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-6.1", f.RuleId);
        Assert.Equal("AGG1", f.RelatedFeatureId);
    }

    [Fact]
    public void AggregationHasMembers_FailsOnSinglePeer()
    {
        var a = Structure("A");
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(a),
            associations: ImmutableArray.Create(Association("ASOC1", a)));
        Assert.Single(S201AtonRules.AggregationHasMembers.Evaluate(inv, ValidationContext.Default));
    }

    // ── S201-R-6.2 — aggregation members resolved ────────────────

    [Fact]
    public void AggregationMembersResolved_PassesWhenAllResolved()
    {
        var a = Structure("A");
        var b = Structure("B");
        var agg = Aggregation("AGG1", a, b);
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(a, b),
            aggregations: ImmutableArray.Create(agg),
            sourceFeatures: ImmutableArray.Create(
                a.Source,
                b.Source,
                AggregationSource("AGG1", "AtonAggregation", peerCount: 2)));
        Assert.Empty(S201AtonRules.AggregationMembersResolved.Evaluate(inv, ValidationContext.Default));
    }

    [Fact]
    public void AggregationMembersResolved_FailsWhenSomeDropped()
    {
        var a = Structure("A");
        var b = Structure("B");
        var agg = Aggregation("AGG1", a, b);
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(a, b),
            aggregations: ImmutableArray.Create(agg),
            sourceFeatures: ImmutableArray.Create(
                a.Source,
                b.Source,
                AggregationSource("AGG1", "AtonAggregation", peerCount: 4)));
        var f = Assert.Single(S201AtonRules.AggregationMembersResolved.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("S201-R-6.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("AGG1", f.RelatedFeatureId);
        Assert.Contains("2 unresolved", f.Message);
    }

    [Fact]
    public void AggregationMembersResolved_FiresOnAssociation()
    {
        var a = Structure("A");
        var asoc = Association("ASOC1", a);
        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(a),
            associations: ImmutableArray.Create(asoc),
            sourceFeatures: ImmutableArray.Create(
                a.Source,
                AggregationSource("ASOC1", "AtonAssociation", peerCount: 3)));
        var f = Assert.Single(S201AtonRules.AggregationMembersResolved.Evaluate(inv, ValidationContext.Default));
        Assert.Equal("ASOC1", f.RelatedFeatureId);
    }

    // ── Default rule set smoke tests ──────────────────────────────

    [Fact]
    public void DefaultRuleSet_HasAllExpectedRules()
    {
        var ids = S201AtonRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Equal(10, ids.Count);
        Assert.Contains("S201-R-1.1", ids);
        Assert.Contains("S201-R-1.2", ids);
        Assert.Contains("S201-R-1.3", ids);
        Assert.Contains("S201-R-2.1", ids);
        Assert.Contains("S201-R-2.2", ids);
        Assert.Contains("S201-R-3.1", ids);
        Assert.Contains("S201-R-4.1", ids);
        Assert.Contains("S201-R-5.1", ids);
        Assert.Contains("S201-R-6.1", ids);
        Assert.Contains("S201-R-6.2", ids);
    }

    [Fact]
    public void DefaultRuleSet_ProducesNoFindingsOnCleanInventory()
    {
        var host = Structure("HOST", lat: 10, lon: 20);
        var eq = Equipment("EQ", lat: 10, lon: 20, host: host);
        var ais = Ais("AIS-P", AisAtonKind.Physical, "992341001", lat: 10, lon: 20);
        var aggSource = AggregationSource("AGG1", "AtonAggregation", peerCount: 2);
        var agg = Aggregation("AGG1", host, eq);
        var status = StatusInfo("STAT1", changeTypes: 2);

        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(host, eq, ais),
            statusInfo: ImmutableArray.Create(status),
            aggregations: ImmutableArray.Create(agg),
            sourceFeatures: ImmutableArray.Create(host.Source, eq.Source, ais.Source, aggSource));

        var report = S201AtonRules.Validate(inv);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void DefaultRuleSet_SurfacesMultipleViolations()
    {
        var bad = Structure("BAD", lat: 95.0, lon: 0);
        var dup = Structure("BAD");
        var orphanEq = Equipment("ORPHAN", host: null);
        var badAis = Ais("AIS-P", AisAtonKind.Physical, mmsi: null);
        var badStatus = StatusInfo("STAT1", changeTypes: 99);

        var inv = Inventory(
            atons: ImmutableArray.Create<S201AtonObject>(bad, dup, orphanEq, badAis),
            statusInfo: ImmutableArray.Create(badStatus));

        var report = S201AtonRules.Validate(inv);
        var ruleIds = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S201-R-1.1", ruleIds);
        Assert.Contains("S201-R-1.2", ruleIds);
        Assert.Contains("S201-R-2.1", ruleIds);
        Assert.Contains("S201-R-3.1", ruleIds);
        Assert.Contains("S201-R-5.1", ruleIds);
    }
}
