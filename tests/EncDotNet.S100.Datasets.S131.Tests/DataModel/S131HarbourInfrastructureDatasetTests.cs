using System.Collections.Immutable;
using System.Linq;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S131.Tests.DataModel;

public class S131HarbourInfrastructureDatasetTests
{
    private static string TestDataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", filename);

    private static S131Dataset Open(string filename) => S131Dataset.Open(TestDataPath(filename));

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        var empty = new S131Dataset
        {
            ProductIdentifier = "S-131",
            Features = ImmutableArray<S131Feature>.Empty,
            InformationTypes = ImmutableArray<S131InformationType>.Empty,
        };

        Assert.Throws<InvalidOperationException>(() =>
            S131HarbourInfrastructureDataset.From(empty, out _));
    }

    [Fact]
    public void From_NullDataset_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            S131HarbourInfrastructureDataset.From(null!, out _));
    }

    [Fact]
    public void From_TypedFixture_FamilyDiscriminationIsCorrect()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out _);

        Assert.Single(typed.HarbourInfrastructure, h => h.Kind == S131HarbourInfrastructureKind.Bollard);
        Assert.Contains(typed.LayoutFeatures, l => l.Kind == S131LayoutKind.AnchorageArea);
        Assert.Contains(typed.LayoutFeatures, l => l.Kind == S131LayoutKind.FenderLine);
        Assert.Contains(typed.LayoutFeatures, l => l.Kind == S131LayoutKind.Berth);
        Assert.Single(typed.MetadataFeatures, m => m.Kind == S131MetadataKind.DataCoverage);
        Assert.Single(typed.OtherFeatures, o => o.FeatureType == "MysteryFeature");

        // Family enum consistency
        foreach (var f in typed.Features)
        {
            var expected = f switch
            {
                S131HarbourInfrastructure => S131FeatureFamily.HarbourInfrastructure,
                S131LayoutFeature => S131FeatureFamily.Layout,
                S131MetadataFeature => S131FeatureFamily.Metadata,
                _ => S131FeatureFamily.Unknown,
            };
            Assert.Equal(expected, f.Family);
        }
    }

    [Fact]
    public void From_TypedFixture_GeometryShapesArePreserved()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out _);

        var bollard = typed.HarbourInfrastructure.Single(h => h.Id == "f-bollard");
        Assert.Equal(S131GeometryType.Point, bollard.Geometry.GeometryType);
        Assert.Equal(44.6475, bollard.Geometry.Points[0].Latitude, 4);
        Assert.Equal(-63.5713, bollard.Geometry.Points[0].Longitude, 4);

        var anchorage = typed.LayoutFeatures.Single(l => l.Id == "f-anchorage");
        Assert.Equal(S131GeometryType.Surface, anchorage.Geometry.GeometryType);
        Assert.Equal(5, anchorage.Geometry.ExteriorRing.Length);
        Assert.Single(anchorage.Geometry.InteriorRings);
        Assert.Equal(5, anchorage.Geometry.InteriorRings[0].Length);

        var fender = typed.LayoutFeatures.Single(l => l.Id == "f-fender");
        Assert.Equal(S131GeometryType.Curve, fender.Geometry.GeometryType);
        Assert.Single(fender.Geometry.Curves);
        Assert.Equal(3, fender.Geometry.Curves[0].Length);
    }

    [Fact]
    public void From_TypedFixture_AuthorityShortcutsResolveToTypedPeers()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out _);

        var authority = Assert.Single(typed.Authorities);
        Assert.Equal("info-authority", authority.Id);
        Assert.NotNull(authority.ContactDetails);
        Assert.Equal("info-contact", authority.ContactDetails!.Id);
        Assert.NotNull(authority.Applicability);
        Assert.Equal("info-applic", authority.Applicability!.Id);

        // Both shortcut peers also appear in the resolved references list.
        Assert.Contains(authority.ResolvedReferences, r => r.Role == "contactDetails" && r.Target is S131ContactDetails);
        Assert.Contains(authority.ResolvedReferences, r => r.Role == "applicability" && r.Target is S131Applicability);
    }

    [Fact]
    public void From_TypedFixture_AuthorityHasNoGeometryAndIsTolerated()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out _);

        var authority = Assert.Single(typed.Authorities);
        // Authority is modelled as an information type — it has no Geometry surface;
        // its presence in InformationTypes (not Features) is the contract.
        Assert.Contains(authority, typed.InformationTypes);
        Assert.DoesNotContain(typed.Features, f => f.Id == "info-authority");
    }

    [Fact]
    public void From_TypedFixture_FeatureXlinkResolvesToInformationType()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out _);

        var bollard = typed.HarbourInfrastructure.Single(h => h.Id == "f-bollard");
        var applicRef = Assert.Single(bollard.ResolvedReferences);
        Assert.Equal("applicability", applicRef.Role);
        Assert.Equal("info-applic", applicRef.TargetRef);
        var applic = Assert.IsType<S131Applicability>(applicRef.Target);
        Assert.Equal("info-applic", applic.Id);
    }

    [Fact]
    public void From_TypedFixture_DanglingXlinkReportsDiagnostics()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out var diags);

        var dangling = typed.LayoutFeatures.Single(l => l.Id == "f-dangling");
        var r = Assert.Single(dangling.ResolvedReferences);
        Assert.Equal("missing-target", r.TargetRef);
        Assert.Null(r.Target);

        Assert.Contains(diags, d => d.Code == "xlink.unresolved" && d.RelatedId == "f-dangling");
        Assert.Contains(diags, d => d.Code == "s131.reference.dangling" && d.RelatedId == "f-dangling");
    }

    [Fact]
    public void From_TypedFixture_UnknownFeatureCodeEmitsDiagnosticAndFallback()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out var diags);

        // The fixture's MysteryFeature is unknown to the FC enumeration; the
        // raw reader's InformationTypeCodes set also captures MysteryInformation
        // as a feature (anything not in the hard-coded info-type list is a
        // feature). Both surface as S131OtherFeature with the
        // s131.feature.unknown diagnostic.
        Assert.Equal(2, typed.OtherFeatures.Length);
        Assert.Contains(typed.OtherFeatures, o => o.FeatureType == "MysteryFeature" && o.Id == "f-unknown");
        Assert.Contains(diags, d => d.Code == "s131.feature.unknown" && d.RelatedId == "f-unknown");
        Assert.All(typed.OtherFeatures, o => Assert.Equal(S131FeatureFamily.Unknown, o.Family));
    }

    [Fact]
    public void From_UnknownInformationType_FallsBackWithDiagnostic()
    {
        // The raw reader recognises only a hard-coded set of information
        // type codes; future codes must still project cleanly. Construct
        // the raw dataset directly so we can exercise the typed
        // s131.information.unknown path independently of the reader.
        var raw = new S131Dataset
        {
            ProductIdentifier = "S-131",
            Features = ImmutableArray<S131Feature>.Empty,
            InformationTypes = ImmutableArray.Create(new S131InformationType
            {
                Id = "info-mystery",
                TypeCode = "MysteryInformation",
                Attributes = ImmutableDictionary<string, string>.Empty,
                ComplexAttributes = ImmutableArray<S131ComplexAttribute>.Empty,
            }),
        };

        var typed = S131HarbourInfrastructureDataset.From(raw, out var diags);

        var info = Assert.IsType<S131OtherInformationType>(Assert.Single(typed.InformationTypes));
        Assert.Equal("info-mystery", info.Id);
        Assert.Equal("MysteryInformation", info.TypeCode);
        Assert.Contains(diags, d => d.Code == "s131.information.unknown" && d.RelatedId == "info-mystery");
    }

    [Fact]
    public void From_TypedFixture_DuplicateIdsAreReported()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out var diags);

        // Two features share gml:id "f-bollard" in the fixture (a Bollard and a Berth).
        Assert.Equal(2, typed.Features.Count(f => f.Id == "f-bollard"));
        Assert.Contains(diags, d => d.Code == "s131.id.duplicate" && d.RelatedId == "f-bollard");
    }

    [Fact]
    public void From_TypedFixture_EveryRxNKindProjectsCorrectly()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_typed.gml"), out _);

        Assert.Contains(typed.RxNInformation, r => r.Kind == S131RxNKind.NauticalInformation);
        Assert.Contains(typed.RxNInformation, r => r.Kind == S131RxNKind.Regulations);

        // Each RxN entry's TypeCode must match its source.
        foreach (var rxn in typed.RxNInformation)
        {
            Assert.Equal(rxn.Source.TypeCode, rxn.TypeCode);
        }
    }

    [Fact]
    public void From_XlinkFixture_PointFeatureAndContainerInfoTypes()
    {
        // The existing harbour_xlink.gml fixture exercises a typical
        // Authority + ContactDetails + Applicability + feature graph.
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_xlink.gml"), out var diags);

        // No "unresolved" diagnostics — every xlink in the fixture is satisfied.
        Assert.DoesNotContain(diags, d => d.Code == "xlink.unresolved");

        var berth = Assert.Single(typed.LayoutFeatures);
        Assert.Equal(S131LayoutKind.Berth, berth.Kind);
        var applic = Assert.Single(berth.ResolvedReferences);
        Assert.IsType<S131Applicability>(applic.Target);

        var authority = Assert.Single(typed.Authorities);
        Assert.NotNull(authority.ContactDetails);
        Assert.NotNull(authority.Applicability);
    }

    [Fact]
    public void From_SurfaceFixture_LayoutAnchorageArea()
    {
        var typed = S131HarbourInfrastructureDataset.From(Open("harbour_surface.gml"), out _);

        var area = Assert.Single(typed.LayoutFeatures);
        Assert.Equal(S131LayoutKind.AnchorageArea, area.Kind);
        Assert.Equal(S131GeometryType.Surface, area.Geometry.GeometryType);
        Assert.True(area.Geometry.ExteriorRing.Length >= 4);
    }
}
