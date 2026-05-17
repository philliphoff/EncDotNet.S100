using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S128.DataModel;
using EncDotNet.S100.Datasets.S128.Validation;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.S128.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="S128CatalogueRules"/>. Each test constructs
/// a minimal synthetic <see cref="S128ProductCatalogue"/> in memory (no
/// GML fixtures) and asserts the rule fires (or doesn't) with the
/// expected finding shape.
/// </summary>
public class S128CatalogueRulesTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static S128Feature SourceFeature(string id, string type = "ElectronicProduct") => new()
    {
        Id = id,
        FeatureType = type,
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S128ComplexAttribute>.Empty,
        References = ImmutableArray<S128XlinkReference>.Empty,
    };

    private static S128ElectronicProduct ElectronicProduct(
        string id,
        int? editionNumber = null,
        DateTimeOffset? issueDate = null,
        DateTimeOffset? updateDate = null,
        S128GeometryKind geometryKind = S128GeometryKind.None,
        ImmutableArray<GeoPosition>? coordinates = null,
        ImmutableArray<S128OnlineResource>? onlineResources = null) => new()
    {
        Id = id,
        FeatureType = "ElectronicProduct",
        EditionNumber = editionNumber,
        IssueDate = issueDate,
        UpdateDate = updateDate,
        GeometryKind = geometryKind,
        Coordinates = coordinates ?? ImmutableArray<GeoPosition>.Empty,
        OnlineResources = onlineResources ?? ImmutableArray<S128OnlineResource>.Empty,
        Source = SourceFeature(id),
    };

    private static S128ProducerInformation Producer(string id = "P1") => new()
    {
        Id = id,
        AgencyResponsibleForProduction = "NOAA",
        Source = SourceFeature(id, "ProducerInformation"),
    };

    private static S128DistributorInformation Distributor(string id = "D1") => new()
    {
        Id = id,
        DistributorName = "NOAA Distribution",
        Source = SourceFeature(id, "DistributorInformation"),
    };

    private static S128ProductCatalogue Catalogue(
        ImmutableArray<S128CatalogueEntry>? products = null,
        ImmutableArray<S128ProducerInformation>? producers = null,
        ImmutableArray<S128DistributorInformation>? distributors = null,
        string? datasetId = "DS-1")
    {
        var dataset = new S128Dataset
        {
            DatasetIdentifier = datasetId,
            ProductIdentifier = "S-128",
            Features = ImmutableArray<S128Feature>.Empty,
            InformationTypes = ImmutableArray<S128InformationType>.Empty,
        };

        return new S128ProductCatalogue
        {
            DatasetIdentifier = datasetId,
            ProductIdentifier = "S-128",
            Products = products ?? ImmutableArray<S128CatalogueEntry>.Empty,
            Producers = producers ?? ImmutableArray<S128ProducerInformation>.Empty,
            Distributors = distributors ?? ImmutableArray<S128DistributorInformation>.Empty,
            Source = dataset,
        };
    }

    // ── S128-R-12.1 — edition number ≥ 1 ────────────────────────

    [Fact]
    public void EditionNumberPositive_Passes_WhenAbsent()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1", editionNumber: null)));
        Assert.Empty(S128CatalogueRules.EditionNumberPositive.Evaluate(cat, ValidationContext.Default));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public void EditionNumberPositive_Passes_OnPositiveValue(int edition)
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1", editionNumber: edition)));
        Assert.Empty(S128CatalogueRules.EditionNumberPositive.Evaluate(cat, ValidationContext.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void EditionNumberPositive_Fails_OnNonPositive(int edition)
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD", editionNumber: edition)));
        var findings = S128CatalogueRules.EditionNumberPositive
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.1", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Equal("DS-1", f.DatasetId);
    }

    [Fact]
    public void EditionNumberPositive_FlagsAllOffenders()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("OK", editionNumber: 3),
            ElectronicProduct("BAD1", editionNumber: 0),
            ElectronicProduct("BAD2", editionNumber: -1)));
        var findings = S128CatalogueRules.EditionNumberPositive
            .Evaluate(cat, ValidationContext.Default).ToList();
        Assert.Equal(2, findings.Count);
        Assert.Equal(new[] { "BAD1", "BAD2" }, findings.Select(f => f.RelatedFeatureId).ToArray());
    }

    // ── S128-R-12.2 — issueDate ≤ updateDate ────────────────────

    [Fact]
    public void IssueDateBeforeUpdateDate_Passes_WhenEitherDateAbsent()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1", issueDate: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ElectronicProduct("E2", updateDate: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)),
            ElectronicProduct("E3")));
        Assert.Empty(S128CatalogueRules.IssueDateBeforeUpdateDate.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void IssueDateBeforeUpdateDate_Passes_WhenIssueEqualsUpdate()
    {
        var d = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1", issueDate: d, updateDate: d)));
        Assert.Empty(S128CatalogueRules.IssueDateBeforeUpdateDate.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void IssueDateBeforeUpdateDate_Passes_WhenIssueBeforeUpdate()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                issueDate: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                updateDate: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero))));
        Assert.Empty(S128CatalogueRules.IssueDateBeforeUpdateDate.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void IssueDateBeforeUpdateDate_Fails_WhenIssueAfterUpdate()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD",
                issueDate: new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero),
                updateDate: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero))));
        var findings = S128CatalogueRules.IssueDateBeforeUpdateDate
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.2", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
    }

    // ── S128-R-12.3 — WGS-84 lat/lon bounds ─────────────────────

    [Fact]
    public void CoordinatesInWgs84Range_Passes_WhenNoCoordinates()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1")));
        Assert.Empty(S128CatalogueRules.CoordinatesInWgs84Range.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesInWgs84Range_Passes_OnValidPoints()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                geometryKind: S128GeometryKind.Point,
                coordinates: ImmutableArray.Create(new GeoPosition(45.0, -120.0)))));
        Assert.Empty(S128CatalogueRules.CoordinatesInWgs84Range.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnOutOfRangeLatitude()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD",
                geometryKind: S128GeometryKind.Point,
                coordinates: ImmutableArray.Create(new GeoPosition(95.0, 0.0)))));
        var findings = S128CatalogueRules.CoordinatesInWgs84Range
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.3", f.RuleId);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Contains("latitude", f.Message);
        Assert.Equal(new GeoPosition(95.0, 0.0), f.Point);
    }

    [Fact]
    public void CoordinatesInWgs84Range_Fails_OnOutOfRangeLongitude()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD",
                geometryKind: S128GeometryKind.Point,
                coordinates: ImmutableArray.Create(new GeoPosition(0.0, 181.0)))));
        var findings = S128CatalogueRules.CoordinatesInWgs84Range
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("longitude", f.Message);
    }

    [Fact]
    public void CoordinatesInWgs84Range_FlagsAllOffendingVertices()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                geometryKind: S128GeometryKind.Curve,
                coordinates: ImmutableArray.Create(
                    new GeoPosition(0, 0),
                    new GeoPosition(91, 0),
                    new GeoPosition(0, -181)))));
        var findings = S128CatalogueRules.CoordinatesInWgs84Range
            .Evaluate(cat, ValidationContext.Default).ToList();
        Assert.Equal(2, findings.Count);
    }

    // ── S128-R-12.4 — surface ring closure ──────────────────────

    [Fact]
    public void SurfaceRingClosed_Passes_OnNonSurfaceEntries()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                geometryKind: S128GeometryKind.Point,
                coordinates: ImmutableArray.Create(new GeoPosition(0, 0)))));
        Assert.Empty(S128CatalogueRules.SurfaceRingClosed.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingClosed_Passes_OnClosedSquareRing()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0),
            new GeoPosition(0, 1),
            new GeoPosition(1, 1),
            new GeoPosition(1, 0),
            new GeoPosition(0, 0));
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                geometryKind: S128GeometryKind.Surface,
                coordinates: ring)));
        Assert.Empty(S128CatalogueRules.SurfaceRingClosed.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void SurfaceRingClosed_Fails_WhenFewerThanFourVertices()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0),
            new GeoPosition(0, 1),
            new GeoPosition(1, 1));
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD",
                geometryKind: S128GeometryKind.Surface,
                coordinates: ring)));
        var findings = S128CatalogueRules.SurfaceRingClosed
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.4", f.RuleId);
        Assert.Contains("3 vertices", f.Message);
    }

    [Fact]
    public void SurfaceRingClosed_Fails_WhenRingNotClosed()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0),
            new GeoPosition(0, 1),
            new GeoPosition(1, 1),
            new GeoPosition(1, 0));
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD",
                geometryKind: S128GeometryKind.Surface,
                coordinates: ring)));
        var findings = S128CatalogueRules.SurfaceRingClosed
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("not closed", f.Message);
    }

    [Fact]
    public void SurfaceRingClosed_Fails_WhenSurfaceHasNoCoordinates()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD", geometryKind: S128GeometryKind.Surface)));
        var findings = S128CatalogueRules.SurfaceRingClosed
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Contains("0 vertices", f.Message);
    }

    // ── S128-R-12.5 — unique product IDs ────────────────────────

    [Fact]
    public void UniqueProductIds_Passes_OnAllUniqueIds()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1"), ElectronicProduct("E2"), ElectronicProduct("E3")));
        Assert.Empty(S128CatalogueRules.UniqueProductIds.Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void UniqueProductIds_Fails_OnDuplicateId()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1"), ElectronicProduct("E1"), ElectronicProduct("E2")));
        var findings = S128CatalogueRules.UniqueProductIds
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.5", f.RuleId);
        Assert.Equal(ValidationSeverity.Error, f.Severity);
        Assert.Equal("E1", f.RelatedFeatureId);
    }

    [Fact]
    public void UniqueProductIds_Fails_OnMultipleDuplicates()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1"), ElectronicProduct("E1"),
            ElectronicProduct("E2"), ElectronicProduct("E2"), ElectronicProduct("E2")));
        var findings = S128CatalogueRules.UniqueProductIds
            .Evaluate(cat, ValidationContext.Default).ToList();
        // One finding per duplicate occurrence after the first.
        Assert.Equal(3, findings.Count);
    }

    // ── S128-R-12.6 — onlineResource linkage well-formed ────────

    [Fact]
    public void OnlineResourceLinkageWellFormed_Passes_WhenNoOnlineResources()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1")));
        Assert.Empty(S128CatalogueRules.OnlineResourceLinkageWellFormed
            .Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void OnlineResourceLinkageWellFormed_Passes_WhenLinkageBlank()
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                onlineResources: ImmutableArray.Create(
                    new S128OnlineResource { Linkage = null },
                    new S128OnlineResource { Linkage = "   " }))));
        Assert.Empty(S128CatalogueRules.OnlineResourceLinkageWellFormed
            .Evaluate(cat, ValidationContext.Default));
    }

    [Theory]
    [InlineData("https://charts.noaa.gov/ENCs/US5WA50M.zip")]
    [InlineData("ftp://example.com/path")]
    public void OnlineResourceLinkageWellFormed_Passes_OnAbsoluteUri(string linkage)
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("E1",
                onlineResources: ImmutableArray.Create(
                    new S128OnlineResource { Linkage = linkage }))));
        Assert.Empty(S128CatalogueRules.OnlineResourceLinkageWellFormed
            .Evaluate(cat, ValidationContext.Default));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("relative/path/only")]
    public void OnlineResourceLinkageWellFormed_Fails_OnMalformedLinkage(string linkage)
    {
        var cat = Catalogue(products: ImmutableArray.Create<S128CatalogueEntry>(
            ElectronicProduct("BAD",
                onlineResources: ImmutableArray.Create(
                    new S128OnlineResource { Linkage = linkage }))));
        var findings = S128CatalogueRules.OnlineResourceLinkageWellFormed
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.6", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("BAD", f.RelatedFeatureId);
        Assert.Contains(linkage, f.Message);
    }

    // ── S128-R-12.7 — producer / distributor present ────────────

    [Fact]
    public void ProducerOrDistributorPresent_Passes_WithProducer()
    {
        var cat = Catalogue(producers: ImmutableArray.Create(Producer()));
        Assert.Empty(S128CatalogueRules.ProducerOrDistributorPresent
            .Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void ProducerOrDistributorPresent_Passes_WithDistributorOnly()
    {
        var cat = Catalogue(distributors: ImmutableArray.Create(Distributor()));
        Assert.Empty(S128CatalogueRules.ProducerOrDistributorPresent
            .Evaluate(cat, ValidationContext.Default));
    }

    [Fact]
    public void ProducerOrDistributorPresent_Fails_WhenNeitherPresent()
    {
        var cat = Catalogue();
        var findings = S128CatalogueRules.ProducerOrDistributorPresent
            .Evaluate(cat, ValidationContext.Default).ToList();
        var f = Assert.Single(findings);
        Assert.Equal("S128-R-12.7", f.RuleId);
        Assert.Equal(ValidationSeverity.Warning, f.Severity);
        Assert.Equal("DS-1", f.DatasetId);
    }

    // ── Default rule set + Validate entry point ─────────────────

    [Fact]
    public void Default_ContainsAllSevenRules()
    {
        Assert.Equal(7, S128CatalogueRules.Default.Rules.Length);
        var ids = S128CatalogueRules.Default.Rules.Select(r => r.RuleId).ToHashSet();
        Assert.Contains("S128-R-12.1", ids);
        Assert.Contains("S128-R-12.2", ids);
        Assert.Contains("S128-R-12.3", ids);
        Assert.Contains("S128-R-12.4", ids);
        Assert.Contains("S128-R-12.5", ids);
        Assert.Contains("S128-R-12.6", ids);
        Assert.Contains("S128-R-12.7", ids);
    }

    [Fact]
    public void Validate_OnValidCatalogue_ProducesNoFindings()
    {
        var ring = ImmutableArray.Create(
            new GeoPosition(0, 0), new GeoPosition(0, 1),
            new GeoPosition(1, 1), new GeoPosition(1, 0), new GeoPosition(0, 0));
        var cat = Catalogue(
            products: ImmutableArray.Create<S128CatalogueEntry>(
                ElectronicProduct("E1",
                    editionNumber: 2,
                    issueDate: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    updateDate: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    geometryKind: S128GeometryKind.Surface,
                    coordinates: ring,
                    onlineResources: ImmutableArray.Create(
                        new S128OnlineResource { Linkage = "https://example.com/p1" }))),
            producers: ImmutableArray.Create(Producer()));
        var report = S128CatalogueRules.Validate(cat);
        Assert.True(report.IsValid);
        Assert.Empty(report.Findings);
        Assert.Equal(7, report.RulesEvaluated);
    }

    [Fact]
    public void Validate_OnInvalidCatalogue_AggregatesFindingsFromMultipleRules()
    {
        // Edition 0 (12.1), issue > update (12.2), out-of-range coord (12.3),
        // duplicate id (12.5), bad linkage (12.6), no producer/distributor (12.7).
        var cat = Catalogue(
            products: ImmutableArray.Create<S128CatalogueEntry>(
                ElectronicProduct("DUP",
                    editionNumber: 0,
                    issueDate: new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero),
                    updateDate: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    geometryKind: S128GeometryKind.Point,
                    coordinates: ImmutableArray.Create(new GeoPosition(95.0, 0.0)),
                    onlineResources: ImmutableArray.Create(
                        new S128OnlineResource { Linkage = "not a url" })),
                ElectronicProduct("DUP")));

        var report = S128CatalogueRules.Validate(cat);
        Assert.False(report.IsValid);
        var ids = report.Findings.Select(f => f.RuleId).ToHashSet();
        Assert.Contains("S128-R-12.1", ids);
        Assert.Contains("S128-R-12.2", ids);
        Assert.Contains("S128-R-12.3", ids);
        Assert.Contains("S128-R-12.5", ids);
        Assert.Contains("S128-R-12.6", ids);
        Assert.Contains("S128-R-12.7", ids);
    }

    [Fact]
    public void Validate_ThrowsOnNullCatalogue()
    {
        Assert.Throws<ArgumentNullException>(() => S128CatalogueRules.Validate(null!));
    }
}
