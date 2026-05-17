using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S125.DataModel;
using EncDotNet.S100.Datasets.S125.Validation;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S125.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S125AtonRules"/>. Each test constructs a
/// minimal synthetic <see cref="S125AtonDataset"/> in memory (no GML
/// fixtures) and asserts the rule fires (or doesn't) with the expected
/// finding shape.
/// </summary>
public class S125AtonRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static S125Buoy Buoy(string id, double? lat = 0, double? lon = 0,
        S125AtonStatusInformation? status = null) => new()
    {
        Id = id,
        FeatureType = "LateralBuoy",
        Kind = S125BuoyKind.Lateral,
        Position = (lat is null || lon is null) ? null : new GeoPosition(lat.Value, lon.Value),
        Status = status,
    };

    private static S125AisAton Ais(string id, S125AisKind kind = S125AisKind.Virtual, string? mmsi = "123456789") => new()
    {
        Id = id,
        FeatureType = kind switch
        {
            S125AisKind.Physical => "PhysicalAISAidToNavigation",
            S125AisKind.Synthetic => "SyntheticAISAidToNavigation",
            _ => "VirtualAISAidToNavigation",
        },
        Kind = kind,
        Position = new GeoPosition(0, 0),
        ExtraAttributes = mmsi is null
            ? ImmutableDictionary<string, string>.Empty
            : ImmutableDictionary<string, string>.Empty.Add("mMSICode", mmsi),
    };

    private static S125AtonStatusInformation Status(
        string id,
        int? code = 1,
        S125ChangeType type = S125ChangeType.AdvanceNoticeOfChange,
        S125DateRange? fixedRange = null,
        ImmutableArray<S125DateRange>? periodic = null) => new()
    {
        Id = id,
        ChangeTypeCode = code,
        ChangeType = type,
        FixedDateRange = fixedRange,
        PeriodicDateRanges = periodic ?? ImmutableArray<S125DateRange>.Empty,
    };

    private static S125AtonStatusIndication Indication(string id, GeoPosition? pos) => new()
    {
        Id = id,
        Position = pos,
    };

    private static S125Aggregation Aggregation(string id, params IS125Aid[] members) => new()
    {
        Id = id,
        Kind = S125AggregationKind.Aggregation,
        Members = members.ToImmutableArray(),
    };

    private static S125AtonDataset Dataset(
        ImmutableArray<IS125Aid>? aids = null,
        ImmutableArray<S125AtonStatusInformation>? statusInfo = null,
        ImmutableArray<S125AtonStatusIndication>? indications = null,
        ImmutableArray<S125Aggregation>? aggregations = null,
        string? datasetId = "DS-1")
    {
        var source = new S125Dataset
        {
            DatasetIdentifier = datasetId,
            ProductIdentifier = "S-125",
            Features = ImmutableArray<S125Feature>.Empty,
            InformationTypes = ImmutableArray<S125InformationType>.Empty,
        };
        return new S125AtonDataset
        {
            DatasetIdentifier = datasetId,
            ProductIdentifier = "S-125",
            Aids = aids ?? ImmutableArray<IS125Aid>.Empty,
            StatusInformation = statusInfo ?? ImmutableArray<S125AtonStatusInformation>.Empty,
            StatusIndications = indications ?? ImmutableArray<S125AtonStatusIndication>.Empty,
            SpatialQualities = ImmutableArray<S125SpatialQuality>.Empty,
            Aggregations = aggregations ?? ImmutableArray<S125Aggregation>.Empty,
            OtherFeatures = ImmutableArray<S125OtherFeature>.Empty,
            Source = source,
        };
    }

    // ── S125-R-1.1 — aid lat/lon in range ───────────────────────

    [Fact]
    public void AidLatLonInRange_Passes_OnValidPositions()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(
            Buoy("B1", -89.9, -179.9), Buoy("B2", 89.9, 179.9), Buoy("B3", 0, 0)));
        Assert.Empty(S125AtonRules.AidLatLonInRange.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AidLatLonInRange_IgnoresAidsWithoutPosition()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Buoy("B1", lat: null, lon: null)));
        Assert.Empty(S125AtonRules.AidLatLonInRange.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AidLatLonInRange_Fails_OnOutOfRangeLatitude()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Buoy("BAD", 95.0, 0)));
        var f = Assert.Single(S125AtonRules.AidLatLonInRange.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-1.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Equal("DS-1", f.DatasetId);
        Assert.Contains("latitude", f.Message);
    }

    [Fact]
    public void AidLatLonInRange_Fails_OnOutOfRangeLongitude()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Buoy("BAD", 0, 181.0)));
        var f = Assert.Single(S125AtonRules.AidLatLonInRange.Evaluate(ds, ValidationContext.Default));
        Assert.Contains("longitude", f.Message);
    }

    [Fact]
    public void AidLatLonInRange_Fails_OnBothOutOfRange()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Buoy("BAD", 91.0, 181.0)));
        var f = Assert.Single(S125AtonRules.AidLatLonInRange.Evaluate(ds, ValidationContext.Default));
        Assert.Contains("both out of range", f.Message);
    }

    // ── S125-R-1.2 — unique aid IDs ─────────────────────────────

    [Fact]
    public void AidIdsUnique_Passes_OnDistinctIds()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(
            Buoy("B1"), Buoy("B2"), Buoy("B3")));
        Assert.Empty(S125AtonRules.AidIdsUnique.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AidIdsUnique_Fails_OnDuplicate()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(
            Buoy("B1"), Buoy("B1"), Buoy("B2")));
        var f = Assert.Single(S125AtonRules.AidIdsUnique.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-1.2", f.RuleId);
        Assert.Equal("B1", f.RelatedFeatureId);
    }

    [Fact]
    public void AidIdsUnique_CaseInsensitive()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(
            Buoy("Aid-1"), Buoy("aid-1")));
        Assert.Single(S125AtonRules.AidIdsUnique.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AidIdsUnique_EmitsOneFindingPerDuplicateId()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(
            Buoy("A"), Buoy("A"), Buoy("A"), Buoy("B"), Buoy("B")));
        var findings = S125AtonRules.AidIdsUnique.Evaluate(ds, ValidationContext.Default).ToList();
        Assert.Equal(2, findings.Count);
        var ids = findings.Select(f => f.RelatedFeatureId).ToHashSet();
        Assert.Contains("A", ids);
        Assert.Contains("B", ids);
    }

    // ── S125-R-2.1 — AIS MMSI ───────────────────────────────────

    [Theory]
    [InlineData(S125AisKind.Physical)]
    [InlineData(S125AisKind.Synthetic)]
    [InlineData(S125AisKind.Virtual)]
    public void AisAidHasMmsi_Passes_OnNineDigitMmsi(S125AisKind kind)
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Ais("A1", kind, "992341001")));
        Assert.Empty(S125AtonRules.AisAidHasMmsi.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AisAidHasMmsi_IgnoresNonAisAids()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Buoy("B1")));
        Assert.Empty(S125AtonRules.AisAidHasMmsi.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AisAidHasMmsi_Fails_WhenMissing()
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Ais("AIS1", mmsi: null)));
        var f = Assert.Single(S125AtonRules.AisAidHasMmsi.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-2.1", f.RuleId);
        Assert.Contains("missing", f.Message);
        Assert.Equal("AIS1", f.RelatedFeatureId);
    }

    [Theory]
    [InlineData("12345")]      // too short
    [InlineData("1234567890")] // too long
    [InlineData("12345678A")]  // non-digit
    [InlineData("12345-789")]  // punctuation
    public void AisAidHasMmsi_Fails_WhenMalformed(string mmsi)
    {
        var ds = Dataset(aids: ImmutableArray.Create<IS125Aid>(Ais("AIS1", mmsi: mmsi)));
        var f = Assert.Single(S125AtonRules.AisAidHasMmsi.Evaluate(ds, ValidationContext.Default));
        Assert.Contains("malformed", f.Message);
        Assert.Contains(mmsi, f.Message);
    }

    // ── S125-R-3.1 — changeTypes enumeration ────────────────────

    [Fact]
    public void ChangeTypeCodeInEnumeration_Passes_WhenNoCode()
    {
        var ds = Dataset(statusInfo: ImmutableArray.Create(Status("S1", code: null, type: S125ChangeType.Unknown)));
        Assert.Empty(S125AtonRules.ChangeTypeCodeInEnumeration.Evaluate(ds, ValidationContext.Default));
    }

    [Theory]
    [InlineData(1, S125ChangeType.AdvanceNoticeOfChange)]
    [InlineData(2, S125ChangeType.Discrepancy)]
    [InlineData(3, S125ChangeType.ProposedChange)]
    [InlineData(4, S125ChangeType.TemporaryChange)]
    [InlineData(5, S125ChangeType.PermanentChange)]
    public void ChangeTypeCodeInEnumeration_Passes_OnListedValue(int code, S125ChangeType type)
    {
        var ds = Dataset(statusInfo: ImmutableArray.Create(Status("S1", code: code, type: type)));
        Assert.Empty(S125AtonRules.ChangeTypeCodeInEnumeration.Evaluate(ds, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(99)]
    public void ChangeTypeCodeInEnumeration_Fails_OnOutOfRangeCode(int code)
    {
        // The projection would set ChangeType=Unknown for any out-of-range numeric code.
        var ds = Dataset(statusInfo: ImmutableArray.Create(Status("S1", code: code, type: S125ChangeType.Unknown)));
        var f = Assert.Single(S125AtonRules.ChangeTypeCodeInEnumeration.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-3.1", f.RuleId);
        Assert.Equal("S1", f.RelatedFeatureId);
        Assert.Contains(code.ToString(), f.Message);
    }

    // ── S125-R-3.2 — date range ordering ────────────────────────

    [Fact]
    public void StatusDateRangeOrdered_Passes_WhenAbsent()
    {
        var ds = Dataset(statusInfo: ImmutableArray.Create(Status("S1")));
        Assert.Empty(S125AtonRules.StatusDateRangeOrdered.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void StatusDateRangeOrdered_Passes_WhenStartOnlyOrEndOnly()
    {
        var startOnly = new S125DateRange { Start = DateTimeOffset.UtcNow };
        var endOnly = new S125DateRange { End = DateTimeOffset.UtcNow };
        var ds = Dataset(statusInfo: ImmutableArray.Create(
            Status("S1", fixedRange: startOnly),
            Status("S2", fixedRange: endOnly)));
        Assert.Empty(S125AtonRules.StatusDateRangeOrdered.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void StatusDateRangeOrdered_Passes_WhenStartEqualsEnd()
    {
        var t = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var range = new S125DateRange { Start = t, End = t };
        var ds = Dataset(statusInfo: ImmutableArray.Create(Status("S1", fixedRange: range)));
        Assert.Empty(S125AtonRules.StatusDateRangeOrdered.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void StatusDateRangeOrdered_Fails_OnFixedRangeInverted()
    {
        var range = new S125DateRange
        {
            Start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var ds = Dataset(statusInfo: ImmutableArray.Create(Status("S1", fixedRange: range)));
        var f = Assert.Single(S125AtonRules.StatusDateRangeOrdered.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-3.2", f.RuleId);
        Assert.Equal("S1", f.RelatedFeatureId);
        Assert.Contains("fixedDateRange", f.Message);
    }

    [Fact]
    public void StatusDateRangeOrdered_Fails_OnPeriodicRangeInverted()
    {
        var range = new S125DateRange
        {
            Start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var ds = Dataset(statusInfo: ImmutableArray.Create(
            Status("S1", periodic: ImmutableArray.Create(range))));
        var f = Assert.Single(S125AtonRules.StatusDateRangeOrdered.Evaluate(ds, ValidationContext.Default));
        Assert.Contains("periodicDateRange", f.Message);
    }

    // ── S125-R-4.1 — aggregation members ────────────────────────

    [Fact]
    public void AggregationHasMembers_Passes_OnNonEmpty()
    {
        var ds = Dataset(
            aids: ImmutableArray.Create<IS125Aid>(Buoy("B1")),
            aggregations: ImmutableArray.Create(Aggregation("AGG1", Buoy("B1"))));
        Assert.Empty(S125AtonRules.AggregationHasMembers.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void AggregationHasMembers_Fails_OnEmpty()
    {
        var ds = Dataset(aggregations: ImmutableArray.Create(Aggregation("AGG1")));
        var f = Assert.Single(S125AtonRules.AggregationHasMembers.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-4.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("AGG1", f.RelatedFeatureId);
    }

    // ── S125-R-5.1 — status indication geometry ────────────────

    [Fact]
    public void StatusIndicationHasPosition_Passes_WhenPositioned()
    {
        var ds = Dataset(indications: ImmutableArray.Create(Indication("I1", new GeoPosition(10, 20))));
        Assert.Empty(S125AtonRules.StatusIndicationHasPosition.Evaluate(ds, ValidationContext.Default));
    }

    [Fact]
    public void StatusIndicationHasPosition_Fails_WhenMissing()
    {
        var ds = Dataset(indications: ImmutableArray.Create(Indication("I1", null)));
        var f = Assert.Single(S125AtonRules.StatusIndicationHasPosition.Evaluate(ds, ValidationContext.Default));
        Assert.Equal("S125-R-5.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("I1", f.RelatedFeatureId);
    }

    // ── Default ruleset composition ─────────────────────────────

    [Fact]
    public void Default_ContainsAllSevenRules()
    {
        Assert.Equal(7, S125AtonRules.Default.Rules.Length);
        var ids = S125AtonRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S125-R-1.1", ids);
        Assert.Contains("S125-R-1.2", ids);
        Assert.Contains("S125-R-2.1", ids);
        Assert.Contains("S125-R-3.1", ids);
        Assert.Contains("S125-R-3.2", ids);
        Assert.Contains("S125-R-4.1", ids);
        Assert.Contains("S125-R-5.1", ids);
    }

    [Fact]
    public void Validate_OnValidDataset_ProducesNoFindings()
    {
        var ds = Dataset(
            aids: ImmutableArray.Create<IS125Aid>(
                Buoy("B1", 10, 20),
                Ais("AIS1", S125AisKind.Virtual, "992341001")),
            statusInfo: ImmutableArray.Create(Status("S1", code: 1, type: S125ChangeType.AdvanceNoticeOfChange)),
            indications: ImmutableArray.Create(Indication("I1", new GeoPosition(10, 20))),
            aggregations: ImmutableArray.Create(Aggregation("AGG1", Buoy("B1", 10, 20))));

        var report = S125AtonRules.Validate(ds);
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(7, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidDataset_AggregatesFindingsFromMultipleRules()
    {
        // Out-of-range pos (1.1), duplicate id (1.2), missing MMSI (2.1),
        // bad change code (3.1), inverted range (3.2), empty aggregation (4.1),
        // and missing indication position (5.1).
        var inverted = new S125DateRange
        {
            Start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var ds = Dataset(
            aids: ImmutableArray.Create<IS125Aid>(
                Buoy("B1", 200, 0),
                Buoy("B1", 0, 0),
                Ais("AIS-NoMmsi", S125AisKind.Virtual, mmsi: null)),
            statusInfo: ImmutableArray.Create(
                Status("S1", code: 42, type: S125ChangeType.Unknown),
                Status("S2", code: 1, type: S125ChangeType.AdvanceNoticeOfChange, fixedRange: inverted)),
            indications: ImmutableArray.Create(Indication("I1", null)),
            aggregations: ImmutableArray.Create(Aggregation("AGG1")));

        var report = S125AtonRules.Validate(ds);
        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S125-R-1.1", ids);
        Assert.Contains("S125-R-1.2", ids);
        Assert.Contains("S125-R-2.1", ids);
        Assert.Contains("S125-R-3.1", ids);
        Assert.Contains("S125-R-3.2", ids);
        Assert.Contains("S125-R-4.1", ids);
        Assert.Contains("S125-R-5.1", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullDataset()
    {
        Assert.Throws<ArgumentNullException>(() => S125AtonRules.Validate(null!));
    }

    [Fact]
    public void Validate_PropagatesDatasetIdOntoFindings()
    {
        var ds = Dataset(
            aids: ImmutableArray.Create<IS125Aid>(Buoy("BAD", 95, 0)),
            datasetId: "MY-DATASET");
        var report = S125AtonRules.Validate(ds);
        Assert.All(report.Findings, f => Assert.Equal("MY-DATASET", f.DatasetId));
    }
}
