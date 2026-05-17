using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;
using EncDotNet.S100.Datasets.S124.Validation;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S124.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S124NavigationalWarningRules"/>. Each test
/// constructs a minimal synthetic <see cref="S124NavigationalWarning"/>
/// in memory (no GML fixtures) and asserts the rule fires (or doesn't)
/// with the expected finding shape.
/// </summary>
public class S124NavigationalWarningRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static readonly S124Dataset EmptyDataset = new()
    {
        Features = ImmutableArray<S124Feature>.Empty,
        InformationTypes = ImmutableArray<S124InformationType>.Empty,
    };

    private static S124NavwarnPart Part(
        string id,
        S124GeometryKind kind = S124GeometryKind.None,
        ImmutableArray<GeoPosition>? coords = null,
        string? text = "Notice to mariners.",
        ImmutableArray<S124AffectedArea>? areas = null,
        ImmutableArray<S124TextPlacement>? textPlacements = null) => new()
        {
            Id = id,
            GeometryKind = kind,
            Coordinates = coords ?? ImmutableArray<GeoPosition>.Empty,
            WarningInformation = text,
            AffectedAreas = areas ?? ImmutableArray<S124AffectedArea>.Empty,
            TextPlacements = textPlacements ?? ImmutableArray<S124TextPlacement>.Empty,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

    private static S124AffectedArea Area(
        string id,
        S124GeometryKind kind,
        ImmutableArray<GeoPosition> coords) => new()
        {
            Id = id,
            GeometryKind = kind,
            Coordinates = coords,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

    private static S124TextPlacement TextPlacement(string id, double? lat = null, double? lon = null, string? text = null) => new()
    {
        Id = id,
        Position = (lat.HasValue && lon.HasValue) ? new GeoPosition(lat.Value, lon.Value) : null,
        Text = text,
        ExtraAttributes = ImmutableDictionary<string, string>.Empty,
    };

    private static S124WarningReference Reference(string id, int? category, string? msgRef) => new()
    {
        Id = id,
        ReferenceCategory = category,
        MessageReference = msgRef,
        ExtraAttributes = ImmutableDictionary<string, string>.Empty,
    };

    private static S124NavwarnPreamble Preamble(
        string id = "P1",
        int? warningNumber = 42,
        int? year = 2026,
        string? navarea = null) => new()
        {
            Id = id,
            MessageSeriesIdentifier = new S124MessageSeriesIdentifier
            {
                WarningNumber = warningNumber,
                Year = year,
            },
            NavareaCode = navarea,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };

    private static S124NavigationalWarning Warning(
        S124NavwarnPreamble? preamble = null,
        ImmutableArray<S124NavwarnPart>? parts = null,
        ImmutableArray<S124WarningReference>? references = null,
        bool includeDefaultPreamble = true,
        string? datasetId = "DS-1") => new()
        {
            DatasetIdentifier = datasetId,
            Preamble = preamble ?? (includeDefaultPreamble ? Preamble() : null),
            Parts = parts ?? ImmutableArray.Create(Part("PART-1")),
            References = references ?? ImmutableArray<S124WarningReference>.Empty,
            SpatialQualities = ImmutableArray<S124SpatialQuality>.Empty,
            Source = EmptyDataset,
        };

    private static ImmutableArray<GeoPosition> ClosedRing(double centerLat, double centerLon) =>
        ImmutableArray.Create(
            new GeoPosition(centerLat, centerLon),
            new GeoPosition(centerLat + 0.1, centerLon),
            new GeoPosition(centerLat + 0.1, centerLon + 0.1),
            new GeoPosition(centerLat, centerLon));

    // ── S124-R-1.1 — minimum part count ─────────────────────────

    [Fact]
    public void MinimumPartCount_Passes_WhenAtLeastOnePart()
    {
        var w = Warning(parts: ImmutableArray.Create(Part("P1")));
        Assert.Empty(S124NavigationalWarningRules.MinimumPartCount.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void MinimumPartCount_Fails_WhenNoParts()
    {
        var w = Warning(parts: ImmutableArray<S124NavwarnPart>.Empty);
        var f = Assert.Single(S124NavigationalWarningRules.MinimumPartCount
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-1.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("DS-1", f.DatasetId);
    }

    // ── S124-R-2.1 — preamble required ──────────────────────────

    [Fact]
    public void PreambleRequired_Passes_WhenPresent()
    {
        var w = Warning();
        Assert.Empty(S124NavigationalWarningRules.PreambleRequired.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void PreambleRequired_Fails_WhenAbsent()
    {
        var w = Warning(includeDefaultPreamble: false);
        var f = Assert.Single(S124NavigationalWarningRules.PreambleRequired
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-2.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
    }

    // ── S124-R-2.2 — message series identifier ──────────────────

    [Fact]
    public void MessageSeriesIdentifier_Passes_OnValidNumberAndYear()
    {
        var w = Warning(preamble: Preamble(warningNumber: 1, year: 2026));
        Assert.Empty(S124NavigationalWarningRules.MessageSeriesIdentifierWellFormed
            .Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void MessageSeriesIdentifier_Passes_WhenAbsent()
    {
        var noMsi = new S124NavwarnPreamble
        {
            Id = "P1",
            MessageSeriesIdentifier = null,
            ExtraAttributes = ImmutableDictionary<string, string>.Empty,
        };
        var w = Warning(preamble: noMsi);
        Assert.Empty(S124NavigationalWarningRules.MessageSeriesIdentifierWellFormed
            .Evaluate(w, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MessageSeriesIdentifier_Fails_OnNonPositiveWarningNumber(int wn)
    {
        var w = Warning(preamble: Preamble(warningNumber: wn, year: 2026));
        var f = Assert.Single(S124NavigationalWarningRules.MessageSeriesIdentifierWellFormed
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-2.2", f.RuleId);
        Assert.Contains("warningNumber", f.Message);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2101)]
    public void MessageSeriesIdentifier_Fails_OnImplausibleYear(int year)
    {
        var w = Warning(preamble: Preamble(warningNumber: 1, year: year));
        var f = Assert.Single(S124NavigationalWarningRules.MessageSeriesIdentifierWellFormed
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-2.2", f.RuleId);
        Assert.Contains("year", f.Message);
    }

    // ── S124-R-3.1 — NAVAREA code valid ─────────────────────────

    [Fact]
    public void NavareaCode_Passes_WhenAbsent()
    {
        var w = Warning(preamble: Preamble(navarea: null));
        Assert.Empty(S124NavigationalWarningRules.NavareaCodeValid.Evaluate(w, ValidationContext.Default));
    }

    [Theory]
    [InlineData("I")]
    [InlineData("IV")]
    [InlineData("XII")]
    [InlineData("XXI")]
    [InlineData("xvi")] // case-insensitive
    public void NavareaCode_Passes_OnRecognisedCode(string code)
    {
        var w = Warning(preamble: Preamble(navarea: code));
        Assert.Empty(S124NavigationalWarningRules.NavareaCodeValid.Evaluate(w, ValidationContext.Default));
    }

    [Theory]
    [InlineData("XXII")]
    [InlineData("0")]
    [InlineData("ATLANTIC")]
    public void NavareaCode_Fails_OnUnknownCode(string code)
    {
        var w = Warning(preamble: Preamble(navarea: code));
        var f = Assert.Single(S124NavigationalWarningRules.NavareaCodeValid
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-3.1", f.RuleId);
        Assert.Contains(code, f.Message);
    }

    // ── S124-R-4.1 — coordinates in range ───────────────────────

    [Fact]
    public void CoordinatesInRange_Passes_WhenAllCoordinatesValid()
    {
        var part = Part("P1", S124GeometryKind.Curve, ImmutableArray.Create(
            new GeoPosition(10.0, 20.0), new GeoPosition(11.0, 21.0)));
        var w = Warning(parts: ImmutableArray.Create(part));
        Assert.Empty(S124NavigationalWarningRules.CoordinatesInRange.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesInRange_Fails_OnOutOfRangeLatitude()
    {
        var part = Part("BAD", S124GeometryKind.Point, ImmutableArray.Create(new GeoPosition(95.0, 0.0)));
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.CoordinatesInRange
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-4.1", f.RuleId);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Contains("latitude", f.Message);
    }

    [Fact]
    public void CoordinatesInRange_Fails_OnOutOfRangeLongitudeOnAffectedArea()
    {
        var area = Area("A1", S124GeometryKind.Point, ImmutableArray.Create(new GeoPosition(0, 200.0)));
        var part = Part("P1", areas: ImmutableArray.Create(area));
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.CoordinatesInRange
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("A1", f.RelatedFeatureId);
        Assert.Contains("NavwarnAreaAffected", f.Message);
        Assert.Contains("longitude", f.Message);
    }

    [Fact]
    public void CoordinatesInRange_Fails_OnTextPlacementPosition()
    {
        var tp = TextPlacement("T1", lat: -100, lon: 0, text: "x");
        var part = Part("P1", textPlacements: ImmutableArray.Create(tp));
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.CoordinatesInRange
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("T1", f.RelatedFeatureId);
        Assert.Contains("TextPlacement", f.Message);
    }

    // ── S124-R-4.2 — surface ring closed ────────────────────────

    [Fact]
    public void SurfaceRingClosed_Passes_OnClosedRingWithFourPoints()
    {
        var part = Part("P1", S124GeometryKind.Surface, ClosedRing(10, 20));
        var w = Warning(parts: ImmutableArray.Create(part));
        Assert.Empty(S124NavigationalWarningRules.SurfaceRingClosed.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingClosed_Passes_WhenNoSurfaceGeometries()
    {
        var part = Part("P1", S124GeometryKind.Point, ImmutableArray.Create(new GeoPosition(0, 0)));
        var w = Warning(parts: ImmutableArray.Create(part));
        Assert.Empty(S124NavigationalWarningRules.SurfaceRingClosed.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingClosed_Fails_WhenRingHasFewerThanFourPositions()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(1, 0), new GeoPosition(0, 0));
        var part = Part("P1", S124GeometryKind.Surface, ring);
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.SurfaceRingClosed
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-4.2", f.RuleId);
        Assert.Contains("at least four", f.Message);
    }

    [Fact]
    public void SurfaceRingClosed_Fails_WhenFirstAndLastDiffer()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0),
            new GeoPosition(1, 0),
            new GeoPosition(1, 1),
            new GeoPosition(0, 1));
        var part = Part("P1", S124GeometryKind.Surface, ring);
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.SurfaceRingClosed
            .Evaluate(w, ValidationContext.Default));
        Assert.Contains("not closed", f.Message);
    }

    [Fact]
    public void SurfaceRingClosed_Fails_OnAffectedAreaWithOpenRing()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0),
            new GeoPosition(1, 0),
            new GeoPosition(1, 1),
            new GeoPosition(0, 1));
        var area = Area("A1", S124GeometryKind.Surface, ring);
        var part = Part("P1", areas: ImmutableArray.Create(area));
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.SurfaceRingClosed
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("A1", f.RelatedFeatureId);
    }

    // ── S124-R-5.1 — part has warning text ──────────────────────

    [Fact]
    public void PartHasWarningText_Passes_OnWarningInformation()
    {
        var part = Part("P1", text: "Buoy off station.");
        var w = Warning(parts: ImmutableArray.Create(part));
        Assert.Empty(S124NavigationalWarningRules.PartHasWarningText.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void PartHasWarningText_Passes_OnTextPlacementFallback()
    {
        var tp = TextPlacement("T1", lat: 0, lon: 0, text: "Buoy off station.");
        var part = Part("P1", text: null, textPlacements: ImmutableArray.Create(tp));
        var w = Warning(parts: ImmutableArray.Create(part));
        Assert.Empty(S124NavigationalWarningRules.PartHasWarningText.Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void PartHasWarningText_Fails_OnEmptyEverywhere()
    {
        var part = Part("P-EMPTY", text: "   ");
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.PartHasWarningText
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-5.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("P-EMPTY", f.RelatedFeatureId);
    }

    [Fact]
    public void PartHasWarningText_Fails_WhenTextPlacementsAreBlank()
    {
        var tp = TextPlacement("T1", lat: 0, lon: 0, text: "   ");
        var part = Part("P1", text: null, textPlacements: ImmutableArray.Create(tp));
        var w = Warning(parts: ImmutableArray.Create(part));
        var f = Assert.Single(S124NavigationalWarningRules.PartHasWarningText
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("P1", f.RelatedFeatureId);
    }

    // ── S124-R-6.1 — reference target specified ─────────────────

    [Fact]
    public void ReferenceTargetSpecified_Passes_WhenCategoryAndRefPresent()
    {
        var w = Warning(references: ImmutableArray.Create(
            Reference("R1", category: 1, msgRef: "HYDROLANT 0412/2026")));
        Assert.Empty(S124NavigationalWarningRules.ReferenceTargetSpecified
            .Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void ReferenceTargetSpecified_Passes_WhenCategoryAbsent()
    {
        // Without a referenceCategory the rule cannot say anything, so it must
        // pass — even though messageReference is also absent.
        var w = Warning(references: ImmutableArray.Create(
            Reference("R1", category: null, msgRef: null)));
        Assert.Empty(S124NavigationalWarningRules.ReferenceTargetSpecified
            .Evaluate(w, ValidationContext.Default));
    }

    [Fact]
    public void ReferenceTargetSpecified_Fails_WhenCategorySetButRefMissing()
    {
        var w = Warning(references: ImmutableArray.Create(
            Reference("R-BAD", category: 1, msgRef: null)));
        var f = Assert.Single(S124NavigationalWarningRules.ReferenceTargetSpecified
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("S124-R-6.1", f.RuleId);
        Assert.Equal("R-BAD", f.RelatedFeatureId);
    }

    [Fact]
    public void ReferenceTargetSpecified_Fails_WhenCategorySetButRefBlank()
    {
        var w = Warning(references: ImmutableArray.Create(
            Reference("R-BLANK", category: 2, msgRef: "   ")));
        var f = Assert.Single(S124NavigationalWarningRules.ReferenceTargetSpecified
            .Evaluate(w, ValidationContext.Default));
        Assert.Equal("R-BLANK", f.RelatedFeatureId);
    }

    // ── Default ruleset composition ─────────────────────────────

    [Fact]
    public void Default_ContainsAllEightRules()
    {
        Assert.Equal(8, S124NavigationalWarningRules.Default.Rules.Length);
        var ids = S124NavigationalWarningRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S124-R-1.1", ids);
        Assert.Contains("S124-R-2.1", ids);
        Assert.Contains("S124-R-2.2", ids);
        Assert.Contains("S124-R-3.1", ids);
        Assert.Contains("S124-R-4.1", ids);
        Assert.Contains("S124-R-4.2", ids);
        Assert.Contains("S124-R-5.1", ids);
        Assert.Contains("S124-R-6.1", ids);
    }

    [Fact]
    public void Validate_OnValidWarning_ProducesNoFindings()
    {
        var part = Part("P1", S124GeometryKind.Surface, ClosedRing(10, 20), text: "Buoy off station.");
        var w = Warning(
            preamble: Preamble(warningNumber: 42, year: 2026, navarea: "IV"),
            parts: ImmutableArray.Create(part),
            references: ImmutableArray.Create(Reference("R1", 1, "HYDROLANT 0412/2026")));
        var report = S124NavigationalWarningRules.Validate(w);
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(8, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidWarning_AggregatesFindingsFromMultipleRules()
    {
        // No parts (1.1), missing preamble (2.1), and a bad reference (6.1).
        var w = Warning(
            includeDefaultPreamble: false,
            parts: ImmutableArray<S124NavwarnPart>.Empty,
            references: ImmutableArray.Create(Reference("R-BAD", 1, null)));
        var report = S124NavigationalWarningRules.Validate(w);
        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S124-R-1.1", ids);
        Assert.Contains("S124-R-2.1", ids);
        Assert.Contains("S124-R-6.1", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullWarning()
    {
        Assert.Throws<ArgumentNullException>(() => S124NavigationalWarningRules.Validate(null!));
    }
}
