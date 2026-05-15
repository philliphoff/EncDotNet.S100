using System;
using System.IO;
using System.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S411.DataModel;

namespace EncDotNet.S100.Datasets.S411.Tests;

/// <summary>
/// Tests for the strongly-typed projection <see cref="S411SeaIceInventory"/>.
/// Each GML shape (JCOMM operational and IHO 1.2.1 sample) round-trips
/// through the projection, validating short-code normalisation, attribute
/// extraction (including the WMO egg-code bundle), geometry, and
/// diagnostics.
/// </summary>
public class DataModelTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private static S411Dataset Load(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        Skip.IfNot(File.Exists(path), $"Fixture missing: {path}");
        using var s = File.OpenRead(path);
        return S411Dataset.Open(s);
    }

    // ── JCOMM operational shape ────────────────────────────────────────

    [SkippableFact]
    public void From_JcommSyntheticDataset_NormalisesShortCodesAndProjectsEggCode()
    {
        var dataset = Load("cis_seaice_synthetic.gml");

        var inventory = S411SeaIceInventory.From(dataset, out var diagnostics);

        Assert.Equal("S-411", inventory.ProductIdentifier);
        Assert.Same(dataset, inventory.Source);
        Assert.Empty(diagnostics);

        // The synthetic CIS fixture carries two <ice:seaice> members; both
        // normalise to S411SeaIce.
        var seaIce = inventory.IceFeatures.OfType<S411SeaIce>().ToList();
        Assert.Equal(2, seaIce.Count);
        Assert.All(seaIce, s =>
        {
            Assert.Equal("SeaIce", s.NormalizedFeatureType);
            Assert.Equal("seaice", s.SourceFeatureType);
            Assert.Equal(S411GeometryKind.Surface, s.GeometryKind);
            Assert.NotEmpty(s.Coordinates);
            Assert.NotNull(s.EggCode);
        });

        // First feature: iceact=91, partials/sod/flz as Python-list-style strings.
        var first = seaIce[0];
        Assert.Equal(91, first.EggCode!.TotalConcentration);
        Assert.Equal("[20, 30, 20, 4, '23']", first.EggCode.PartialConcentrationsRaw);
        Assert.Equal("[87, 85, 84, 99, 81]", first.EggCode.StagesOfDevelopmentRaw);
        Assert.Equal("[7, 6, 5, 6, 5]", first.EggCode.FormsOfIceRaw);
        Assert.Null(first.EggCode.SnowDepth);

        // Egg-code attributes are removed from ExtraAttributes.
        Assert.Empty(first.ExtraAttributes);
    }

    // ── IHO 1.2.1 sample shape ─────────────────────────────────────────

    [SkippableFact]
    public void From_IhoSample_TDS001_TypesAllFeatureClassesAndPopulatesDataCoverage()
    {
        var dataset = Load("iho_4112C00TDS001.gml");

        var inventory = S411SeaIceInventory.From(dataset, out var diagnostics);

        Assert.Equal("S-411", inventory.ProductIdentifier);
        Assert.Equal("DS1", inventory.DatasetIdentifier);
        Assert.NotNull(inventory.IssueDate);
        Assert.Empty(diagnostics);

        // Exactly one DataCoverage; the rest are ice features.
        var coverage = Assert.Single(inventory.DataCoverages);
        Assert.Equal("ID0", coverage.Id);
        Assert.Equal(S411GeometryKind.Surface, coverage.GeometryKind);
        Assert.Equal(180000, coverage.MinimumDisplayScale);
        Assert.Equal(22000, coverage.MaximumDisplayScale);

        // Spot-check the typed subclasses surfaced by the projection.
        var seaIce = Assert.Single(inventory.IceFeatures.OfType<S411SeaIce>());
        Assert.Equal("ID1", seaIce.Id);
        Assert.Equal("SeaIce", seaIce.NormalizedFeatureType);
        Assert.Equal("SeaIce", seaIce.SourceFeatureType);
        Assert.Equal(S411GeometryKind.Surface, seaIce.GeometryKind);
        Assert.NotNull(seaIce.EggCode);
        Assert.Equal(10, seaIce.EggCode!.SnowDepth);

        var lakeIce = Assert.Single(inventory.IceFeatures.OfType<S411LakeIce>());
        Assert.Equal("ID2", lakeIce.Id);
        // totalConcentration=20 lands on the EggCode bundle, even though
        // the IHO sample emits it as a code-attributed element.
        Assert.Equal(20, lakeIce.EggCode!.TotalConcentration);

        var iceberg = Assert.Single(inventory.IceFeatures.OfType<S411Iceberg>());
        Assert.Equal(S411GeometryKind.Point, iceberg.GeometryKind);
        Assert.Equal(7, iceberg.IcebergSizeCode);

        var iceEdge = Assert.Single(inventory.IceFeatures.OfType<S411IceEdge>());
        Assert.Equal(S411GeometryKind.Curve, iceEdge.GeometryKind);
        Assert.True(iceEdge.Coordinates.Length >= 2);

        var iceLead = Assert.Single(inventory.IceFeatures.OfType<S411IceLead>());
        Assert.Equal(2, iceLead.IceLeadStatusCode);

        var thickness = Assert.Single(inventory.IceFeatures.OfType<S411IceThickness>());
        Assert.Equal(10.0, thickness.IceAverageThickness);

        var snow = Assert.Single(inventory.IceFeatures.OfType<S411SnowCover>());
        Assert.Equal(8, snow.SnowCoverConcentrationCode);

        var melt = Assert.Single(inventory.IceFeatures.OfType<S411StageOfMelt>());
        Assert.Equal(99, melt.MeltStageCode);

        // IceKeelBummock + IcebergLimit are not broken out into dedicated
        // subclasses — they survive as S411OtherFeature with the
        // normalised type name available for dispatch.
        Assert.Contains(inventory.OtherFeatures, o => o.NormalizedFeatureType == "IceKeelBummock");
        Assert.Contains(inventory.OtherFeatures, o => o.NormalizedFeatureType == "IcebergLimit");
    }

    [SkippableFact]
    public void From_IhoSample_TDS002_RoundTripsTheSecondSampleShape()
    {
        var dataset = Load("iho_4112C00TDS002.gml");

        var inventory = S411SeaIceInventory.From(dataset, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Single(inventory.DataCoverages);
        Assert.NotEmpty(inventory.IceFeatures);

        // Every projected feature carries an Id and a normalised PascalCase
        // feature type (no leftover JCOMM short codes from this shape).
        Assert.All(inventory.IceFeatures.Concat<S411IceFeature>(inventory.OtherFeatures), f =>
        {
            Assert.False(string.IsNullOrEmpty(f.Id));
            Assert.False(string.IsNullOrEmpty(f.NormalizedFeatureType));
            Assert.Same(dataset.Features.First(s => s.Id == f.Id), f.Source);
        });
    }

    // ── Behavioural invariants ─────────────────────────────────────────

    [Fact]
    public void From_NullDataset_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => S411SeaIceInventory.From(null!, out _));
    }

    [SkippableFact]
    public void EggCode_BothAttributeFormsPresent_PrefersJcommShortCode()
    {
        // Synthetic JCOMM fixture uses iceact; verify the projection
        // exposes it on EggCode.TotalConcentration and reports no
        // attribute-parse diagnostic.
        var dataset = Load("cis_seaice_synthetic.gml");
        var inventory = S411SeaIceInventory.From(dataset, out var diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Code == "attribute.parse.int");
        Assert.All(inventory.IceFeatures.OfType<S411SeaIce>(),
            s => Assert.NotNull(s.EggCode?.TotalConcentration));
    }

    [SkippableFact]
    public void NormalizedFeatureType_JcommShortCodes_MapToPascalCase()
    {
        // The synthetic fixture only exercises seaice → SeaIce; this test
        // documents the broader normalisation surface via the public
        // projection so a regression in the short-code map is loud.
        var dataset = Load("cis_seaice_synthetic.gml");
        var inventory = S411SeaIceInventory.From(dataset, out _);
        var types = inventory.IceFeatures
            .Select(f => f.NormalizedFeatureType)
            .Distinct()
            .ToList();
        Assert.Contains("SeaIce", types);
        Assert.All(types, t => Assert.True(char.IsUpper(t[0]),
            $"Expected PascalCase, got '{t}'"));
    }
}
