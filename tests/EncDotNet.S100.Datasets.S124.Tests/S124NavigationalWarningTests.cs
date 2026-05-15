using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S124.DataModel;

namespace EncDotNet.S100.Datasets.S124.Tests;

public class S124NavigationalWarningTests
{
    private const string TestDataDir = "TestData";

    private static S124Dataset Load(string fileName)
    {
        var path = Path.Combine(TestDataDir, fileName);
        Assert.True(File.Exists(path), $"Test data file not found: {path}");
        return S124Dataset.Open(path);
    }

    private static S124NavigationalWarning Project(string fileName, out IReadOnlyList<ProjectionDiagnostic> diagnostics)
        => S124NavigationalWarning.From(Load(fileName), out diagnostics);

    [Fact]
    public void Mixed_PreservesDatasetIdentifiers()
    {
        var w = Project("navwarn_mixed.gml", out _);
        Assert.Equal("S-124", w.ProductIdentifier);
        Assert.Equal("DS_NavWarn_Mixed_Test", w.DatasetIdentifier);
    }

    [Fact]
    public void Mixed_ProjectsPreambleAndMessageSeriesIdentifier()
    {
        var w = Project("navwarn_mixed.gml", out _);
        Assert.NotNull(w.Preamble);
        Assert.Equal("info1", w.Preamble!.Id);
        Assert.Equal("North Sea", w.Preamble.GeneralArea);
        Assert.Equal("Approaches to Rotterdam", w.Preamble.Locality);

        var msi = w.Preamble.MessageSeriesIdentifier;
        Assert.NotNull(msi);
        Assert.Equal(305, msi!.WarningNumber);
        Assert.Equal(2026, msi.Year);
        Assert.Equal("NGA", msi.ProductionAgency);
    }

    [Fact]
    public void Mixed_ProjectsNavwarnPartsAndIgnoresOtherFeatures()
    {
        var w = Project("navwarn_mixed.gml", out _);
        Assert.NotEmpty(w.Parts);
        Assert.All(w.Parts, p => Assert.NotNull(p.Id));
        // The mixed fixture has two NavwarnPart features.
        Assert.True(w.Parts.Length >= 2);
    }

    [Fact]
    public void Mixed_ExtractsWarningInformationAndRestriction()
    {
        var w = Project("navwarn_mixed.gml", out _);
        var part = w.Parts.First(p => p.Id == "f1");
        Assert.Equal(7, part.Restriction);
        Assert.Contains("Uncharted obstruction", part.WarningInformation);
    }

    [Fact]
    public void Mixed_ProjectsReferencesInfoType()
    {
        var w = Project("navwarn_mixed.gml", out _);
        var r = Assert.Single(w.References);
        Assert.Equal(2, r.ReferenceCategory);
        Assert.Equal("HYDROLANT 0412/2026", r.MessageReference);
    }

    [Fact]
    public void Mixed_ProjectsSpatialQuality()
    {
        var w = Project("navwarn_mixed.gml", out _);
        var sq = Assert.Single(w.SpatialQualities);
        Assert.Equal(2, sq.QualityOfPosition);
    }

    [Fact]
    public void Point_PartHasPointGeometry()
    {
        var w = Project("navwarn_point.gml", out _);
        var part = w.Parts.First();
        Assert.Equal(S124GeometryKind.Point, part.GeometryKind);
        Assert.Single(part.Coordinates);
    }

    [Fact]
    public void Surface_DatasetProjectsAtLeastOnePart()
    {
        var w = Project("navwarn_surface.gml", out var diagnostics);
        Assert.NotEmpty(w.Parts);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Curve_PartHasCurveGeometry()
    {
        var w = Project("navwarn_curve.gml", out _);
        var part = w.Parts.First();
        Assert.Equal(S124GeometryKind.Curve, part.GeometryKind);
        Assert.True(part.Coordinates.Length >= 2);
    }

    // ── xlink projection ─────────────────────────────────────────

    [Fact]
    public void Xlinks_ResolvesAffectedAreaAndTextPlacement()
    {
        var w = Project("navwarn_with_xlinks.gml", out var diagnostics);

        var part = Assert.Single(w.Parts);
        var area = Assert.Single(part.AffectedAreas);
        Assert.Equal("area1", area.Id);
        Assert.Equal(S124GeometryKind.Surface, area.GeometryKind);
        Assert.Equal(2, area.Restriction);

        var text = Assert.Single(part.TextPlacements);
        Assert.Equal("text1", text.Id);
        Assert.Equal("Test warning label", text.Text);
        Assert.NotNull(text.Position);

        // The "#nope" xlink must produce an unresolved diagnostic.
        Assert.Contains(diagnostics, d => d.Code == "xlink.unresolved");
    }

    [Fact]
    public void Xlinks_PreservesUnknownAttributesAsExtras()
    {
        var w = Project("navwarn_with_xlinks.gml", out _);
        // restriction is consumed; nothing else should leak through ExtraAttributes for the part.
        var part = Assert.Single(w.Parts);
        Assert.DoesNotContain("restriction", part.ExtraAttributes.Keys);
    }

    [Fact]
    public void EmptyDataset_Throws()
    {
        // Build a fully empty dataset; only this case throws.
        var empty = new S124Dataset
        {
            ProductIdentifier = "S-124",
            DatasetIdentifier = "x",
            Features = System.Collections.Immutable.ImmutableArray<S124Feature>.Empty,
            InformationTypes = System.Collections.Immutable.ImmutableArray<S124InformationType>.Empty,
        };
        Assert.Throws<InvalidOperationException>(() =>
            S124NavigationalWarning.From(empty, out _));
    }

    [Fact]
    public void From_ThrowsOnNullDataset()
    {
        Assert.Throws<ArgumentNullException>(() =>
            S124NavigationalWarning.From(null!, out _));
    }
}
