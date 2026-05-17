using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S411.DataModel;
using EncDotNet.S100.Datasets.S411.Validation;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S411.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S411SeaIceRules"/>. Each test constructs a
/// minimal synthetic <see cref="S411SeaIceInventory"/> in memory (no GML
/// fixtures) and asserts the rule fires (or doesn't) with the expected
/// finding shape.
/// </summary>
public class S411SeaIceRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static readonly S411Dataset EmptyDataset = new()
    {
        Features = ImmutableArray<S411Feature>.Empty,
        SourceDocument = new System.Xml.Linq.XDocument(),
    };

    private static readonly S411Feature DummySource = new()
    {
        Id = "_dummy",
        FeatureType = "SeaIce",
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S411ComplexAttribute>.Empty,
    };

    private static ImmutableArray<GeoPosition> Coords(params (double lat, double lon)[] pts) =>
        pts.Select(p => new GeoPosition(p.lat, p.lon)).ToImmutableArray();

    private static S411SeaIce SeaIce(
        string id,
        S411GeometryKind kind = S411GeometryKind.Surface,
        ImmutableArray<GeoPosition>? coordinates = null,
        S411EggCode? eggCode = null) => new()
    {
        Id = id,
        NormalizedFeatureType = "SeaIce",
        SourceFeatureType = "SeaIce",
        GeometryKind = kind,
        Coordinates = coordinates ?? ImmutableArray<GeoPosition>.Empty,
        EggCode = eggCode,
        Source = DummySource,
    };

    private static S411LakeIce LakeIce(string id, S411EggCode? eggCode = null) => new()
    {
        Id = id,
        NormalizedFeatureType = "LakeIce",
        SourceFeatureType = "LakeIce",
        GeometryKind = S411GeometryKind.Surface,
        Coordinates = ClosedSquare(),
        EggCode = eggCode,
        Source = DummySource,
    };

    private static S411Iceberg Iceberg(string id, int? sizeCode = null) => new()
    {
        Id = id,
        NormalizedFeatureType = "Iceberg",
        SourceFeatureType = "Iceberg",
        GeometryKind = S411GeometryKind.Point,
        Coordinates = Coords((45, -50)),
        IcebergSizeCode = sizeCode,
        Source = DummySource,
    };

    private static S411IceEdge IceEdge(string id, ImmutableArray<GeoPosition> coords) => new()
    {
        Id = id,
        NormalizedFeatureType = "IceEdge",
        SourceFeatureType = "IceEdge",
        GeometryKind = S411GeometryKind.Curve,
        Coordinates = coords,
        Source = DummySource,
    };

    private static S411IceThickness IceThickness(string id, double? thickness) => new()
    {
        Id = id,
        NormalizedFeatureType = "IceThickness",
        SourceFeatureType = "IceThickness",
        GeometryKind = S411GeometryKind.Point,
        Coordinates = Coords((45, -50)),
        IceAverageThickness = thickness,
        Source = DummySource,
    };

    private static S411DataCoverage DataCoverage(string id, ImmutableArray<GeoPosition> coords) => new()
    {
        Id = id,
        NormalizedFeatureType = "DataCoverage",
        SourceFeatureType = "DataCoverage",
        GeometryKind = S411GeometryKind.Surface,
        Coordinates = coords,
        Source = DummySource,
    };

    private static ImmutableArray<GeoPosition> ClosedSquare() =>
        Coords((0, 0), (0, 1), (1, 1), (1, 0), (0, 0));

    private static S411SeaIceInventory Inventory(
        IEnumerable<S411IceFeature>? ice = null,
        IEnumerable<S411DataCoverage>? coverages = null,
        IEnumerable<S411OtherFeature>? others = null) => new()
    {
        IceFeatures = ice?.ToImmutableArray() ?? ImmutableArray<S411IceFeature>.Empty,
        DataCoverages = coverages?.ToImmutableArray() ?? ImmutableArray<S411DataCoverage>.Empty,
        OtherFeatures = others?.ToImmutableArray() ?? ImmutableArray<S411OtherFeature>.Empty,
        Source = EmptyDataset,
    };

    // ── S411-R-3.1 — WGS-84 lat/lon range ────────────────────────

    [Fact]
    public void CoordinatesInWgs84Range_Passes_ForValidCoordinates()
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", coordinates: ClosedSquare()) });
        var findings = S411SeaIceRules.CoordinatesInWgs84Range.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(-90.5, 0.0)]
    [InlineData(0.0, 181.0)]
    [InlineData(0.0, -200.0)]
    public void CoordinatesInWgs84Range_Fails_WhenOutOfRange(double lat, double lon)
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", S411GeometryKind.Point, Coords((lat, lon))) });
        var findings = S411SeaIceRules.CoordinatesInWgs84Range
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-3.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("F1", f.RelatedFeatureId);
        Assert.NotNull(f.Point);
    }

    [Fact]
    public void CoordinatesInWgs84Range_ReportsBothAxes_WhenBothInvalid()
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", S411GeometryKind.Point, Coords((200, 200))) });
        var findings = S411SeaIceRules.CoordinatesInWgs84Range
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("both out of range", f.Message);
    }

    [Fact]
    public void CoordinatesInWgs84Range_AlsoChecksDataCoveragesAndOthers()
    {
        var inv = Inventory(
            coverages: new[] { DataCoverage("C1", Coords((100, 0), (0, 0), (0, 1), (100, 0))) });
        var findings = S411SeaIceRules.CoordinatesInWgs84Range
            .Evaluate(inv, ValidationContext.Default).ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("C1", f.RelatedFeatureId));
    }

    // ── S411-R-3.2 — surface polygon closure ─────────────────────

    [Fact]
    public void SurfacePolygonClosed_Passes_ForClosedSquare()
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", coordinates: ClosedSquare()) });
        var findings = S411SeaIceRules.SurfacePolygonClosed.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void SurfacePolygonClosed_Fails_WhenFewerThanFourPoints()
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("F1", coordinates: Coords((0, 0), (0, 1), (0, 0))),
        });
        var findings = S411SeaIceRules.SurfacePolygonClosed
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-3.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Contains("requires ≥ 4", f.Message);
    }

    [Fact]
    public void SurfacePolygonClosed_Fails_WhenNotClosed()
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("F1", coordinates: Coords((0, 0), (0, 1), (1, 1), (1, 0))),
        });
        var findings = S411SeaIceRules.SurfacePolygonClosed
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-3.2", f.RuleId);
        Assert.Contains("not closed", f.Message);
    }

    [Fact]
    public void SurfacePolygonClosed_IgnoresNonSurfaceFeatures()
    {
        var inv = Inventory(ice: new[]
        {
            (S411IceFeature)IceEdge("E1", Coords((0, 0), (1, 1))),
            Iceberg("I1"),
        });
        var findings = S411SeaIceRules.SurfacePolygonClosed.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    // ── S411-R-3.3 — curve minimum vertex count ──────────────────

    [Fact]
    public void CurveHasMinimumVertices_Passes_WhenTwoOrMore()
    {
        var inv = Inventory(ice: new[] { (S411IceFeature)IceEdge("E1", Coords((0, 0), (1, 1))) });
        var findings = S411SeaIceRules.CurveHasMinimumVertices.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CurveHasMinimumVertices_Fails_WhenFewerThanTwo(int n)
    {
        var coords = Enumerable.Range(0, n).Select(i => new GeoPosition(i, i)).ToImmutableArray();
        var inv = Inventory(ice: new[] { (S411IceFeature)IceEdge("E1", coords) });
        var findings = S411SeaIceRules.CurveHasMinimumVertices
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-3.3", f.RuleId);
        Assert.Equal("E1", f.RelatedFeatureId);
    }

    [Fact]
    public void CurveHasMinimumVertices_IgnoresSurfaceFeatures()
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", coordinates: ClosedSquare()) });
        var findings = S411SeaIceRules.CurveHasMinimumVertices.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    // ── S411-R-4.1 — total concentration enumeration ─────────────

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(35)]
    [InlineData(91)]
    [InlineData(99)]
    public void TotalConcentrationInEnumeration_Passes_ForValidCodes(int code)
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("F1", coordinates: ClosedSquare(),
                eggCode: new S411EggCode { TotalConcentration = code }),
        });
        var findings = S411SeaIceRules.TotalConcentrationInEnumeration
            .Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(15)]
    [InlineData(100)]
    public void TotalConcentrationInEnumeration_Fails_ForUnknownCodes(int code)
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("F1", coordinates: ClosedSquare(),
                eggCode: new S411EggCode { TotalConcentration = code }),
        });
        var findings = S411SeaIceRules.TotalConcentrationInEnumeration
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-4.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("F1", f.RelatedFeatureId);
    }

    [Fact]
    public void TotalConcentrationInEnumeration_Skips_WhenEggCodeAbsent()
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", coordinates: ClosedSquare()) });
        var findings = S411SeaIceRules.TotalConcentrationInEnumeration
            .Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void TotalConcentrationInEnumeration_AppliesToLakeIce()
    {
        var inv = Inventory(ice: new[]
        {
            (S411IceFeature)LakeIce("L1", new S411EggCode { TotalConcentration = 11 }),
        });
        var findings = S411SeaIceRules.TotalConcentrationInEnumeration
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("L1", f.RelatedFeatureId);
    }

    // ── S411-R-4.2 — ice average thickness non-negative ──────────

    [Fact]
    public void IceAverageThicknessNonNegative_Passes_WhenZeroOrPositive()
    {
        var inv = Inventory(ice: new[]
        {
            (S411IceFeature)IceThickness("T1", 0.0),
            IceThickness("T2", 2.5),
        });
        var findings = S411SeaIceRules.IceAverageThicknessNonNegative
            .Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void IceAverageThicknessNonNegative_Fails_WhenNegative()
    {
        var inv = Inventory(ice: new[] { (S411IceFeature)IceThickness("T1", -0.5) });
        var findings = S411SeaIceRules.IceAverageThicknessNonNegative
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-4.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("T1", f.RelatedFeatureId);
    }

    [Fact]
    public void IceAverageThicknessNonNegative_Skips_WhenAbsent()
    {
        var inv = Inventory(ice: new[] { (S411IceFeature)IceThickness("T1", null) });
        var findings = S411SeaIceRules.IceAverageThicknessNonNegative
            .Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    // ── S411-R-4.3 — snow depth non-negative ─────────────────────

    [Fact]
    public void SnowDepthNonNegative_Passes_WhenZeroOrPositive()
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("F1", coordinates: ClosedSquare(), eggCode: new S411EggCode { SnowDepth = 0.0 }),
            SeaIce("F2", coordinates: ClosedSquare(), eggCode: new S411EggCode { SnowDepth = 5.5 }),
        });
        var findings = S411SeaIceRules.SnowDepthNonNegative.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void SnowDepthNonNegative_Fails_WhenNegative()
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("F1", coordinates: ClosedSquare(), eggCode: new S411EggCode { SnowDepth = -1.0 }),
        });
        var findings = S411SeaIceRules.SnowDepthNonNegative
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-4.3", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("F1", f.RelatedFeatureId);
    }

    [Fact]
    public void SnowDepthNonNegative_AppliesToLakeIce()
    {
        var inv = Inventory(ice: new[]
        {
            (S411IceFeature)LakeIce("L1", new S411EggCode { SnowDepth = -3.0 }),
        });
        var findings = S411SeaIceRules.SnowDepthNonNegative
            .Evaluate(inv, ValidationContext.Default).ToList();
        Assert.Single(findings);
    }

    // ── S411-R-4.4 — iceberg size code enumeration ───────────────

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(99)]
    public void IcebergSizeInEnumeration_Passes_ForValidCodes(int code)
    {
        var inv = Inventory(ice: new[] { (S411IceFeature)Iceberg("I1", code) });
        var findings = S411SeaIceRules.IcebergSizeInEnumeration
            .Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void IcebergSizeInEnumeration_Fails_ForUnknownCodes(int code)
    {
        var inv = Inventory(ice: new[] { (S411IceFeature)Iceberg("I1", code) });
        var findings = S411SeaIceRules.IcebergSizeInEnumeration
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-4.4", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("I1", f.RelatedFeatureId);
    }

    [Fact]
    public void IcebergSizeInEnumeration_Skips_WhenAbsent()
    {
        var inv = Inventory(ice: new[] { (S411IceFeature)Iceberg("I1", null) });
        var findings = S411SeaIceRules.IcebergSizeInEnumeration
            .Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    // ── S411-R-5.1 — unique feature identifiers ──────────────────

    [Fact]
    public void UniqueFeatureIdentifiers_Passes_WhenAllUnique()
    {
        var inv = Inventory(
            ice: new[]
            {
                SeaIce("F1", coordinates: ClosedSquare()),
                (S411IceFeature)Iceberg("I1"),
            },
            coverages: new[] { DataCoverage("C1", ClosedSquare()) });
        var findings = S411SeaIceRules.UniqueFeatureIdentifiers.Evaluate(inv, ValidationContext.Default);
        Assert.Empty(findings);
    }

    [Fact]
    public void UniqueFeatureIdentifiers_Fails_WhenDuplicatesAcrossLists()
    {
        var inv = Inventory(
            ice: new[] { SeaIce("X1", coordinates: ClosedSquare()) },
            coverages: new[] { DataCoverage("X1", ClosedSquare()) });
        var findings = S411SeaIceRules.UniqueFeatureIdentifiers
            .Evaluate(inv, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S411-R-5.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("X1", f.RelatedFeatureId);
    }

    [Fact]
    public void UniqueFeatureIdentifiers_ReportsEachDuplicateOnce()
    {
        var inv = Inventory(ice: new[]
        {
            SeaIce("X1", coordinates: ClosedSquare()),
            SeaIce("X1", coordinates: ClosedSquare()),
            SeaIce("X1", coordinates: ClosedSquare()),
        });
        var findings = S411SeaIceRules.UniqueFeatureIdentifiers
            .Evaluate(inv, ValidationContext.Default).ToList();
        Assert.Single(findings);
    }

    // ── Default rule set ────────────────────────────────────────

    [Fact]
    public void Default_RuleSet_Contains_AllEightRules()
    {
        var ids = S411SeaIceRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Equal(8, ids.Count);
        Assert.Contains("S411-R-3.1", ids);
        Assert.Contains("S411-R-3.2", ids);
        Assert.Contains("S411-R-3.3", ids);
        Assert.Contains("S411-R-4.1", ids);
        Assert.Contains("S411-R-4.2", ids);
        Assert.Contains("S411-R-4.3", ids);
        Assert.Contains("S411-R-4.4", ids);
        Assert.Contains("S411-R-5.1", ids);
    }

    [Fact]
    public void Validate_Returns_EmptyReport_ForCleanInventory()
    {
        var inv = Inventory(ice: new[] { SeaIce("F1", coordinates: ClosedSquare()) });
        var report = S411SeaIceRules.Validate(inv);
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Validate_Throws_OnNullInventory()
    {
        Assert.Throws<ArgumentNullException>(() => S411SeaIceRules.Validate(null!));
    }
}
