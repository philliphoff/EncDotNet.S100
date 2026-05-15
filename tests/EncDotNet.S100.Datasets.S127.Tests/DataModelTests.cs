using System.Linq;
using EncDotNet.S100.Datasets.S127.DataModel;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S127.Tests;

/// <summary>
/// Tests for the strongly-typed S-127 data model
/// (<see cref="S127MarineServicesDataset"/> and friends).
/// </summary>
public class DataModelTests
{
    private static string GetTestDataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", filename);

    [Fact]
    public void From_MixedDataset_ProjectsTypedFeatures()
    {
        var dataset = S127Dataset.Open(GetTestDataPath("marine_mixed.gml"));

        var typed = S127MarineServicesDataset.From(dataset, out var diagnostics);

        Assert.Equal("S-127", typed.ProductIdentifier);
        Assert.Equal("DS_S127_Mixed_Test", typed.DatasetIdentifier);
        Assert.Equal(5, typed.Features.Length);
        Assert.Empty(diagnostics);

        var pbp = Assert.IsType<S127PilotBoardingPlace>(typed.Features.Single(f => f is S127PilotBoardingPlace));
        Assert.Equal(S127GeometryKind.Point, pbp.GeometryKind);
        Assert.Single(pbp.Coordinates);
        Assert.Equal(40.7000, pbp.Coordinates[0].Latitude, 4);
        Assert.Equal(-74.0500, pbp.Coordinates[0].Longitude, 4);
        Assert.Equal(1, pbp.CategoryOfPilotBoardingPlace);
        Assert.Null(pbp.Authority);

        var routeing = Assert.IsType<S127RouteingMeasure>(typed.Features.Single(f => f is S127RouteingMeasure));
        Assert.Equal(S127GeometryKind.Curve, routeing.GeometryKind);
        Assert.Equal(3, routeing.Coordinates.Length);

        var restricted = Assert.IsType<S127RegulatedArea>(typed.Features.Single(f => f is S127RegulatedArea));
        Assert.Equal(S127RegulatedAreaKind.RestrictedArea, restricted.Kind);
        Assert.Equal(S127GeometryKind.Surface, restricted.GeometryKind);
        Assert.Equal(14, restricted.CategoryCode);

        var signal = Assert.IsType<S127SignalStation>(typed.Features.Single(f => f is S127SignalStation));
        Assert.Equal(S127SignalStationKind.Traffic, signal.Kind);

        // Authority is geometry-less.
        var authority = Assert.Single(typed.Authorities);
        Assert.Equal(S127GeometryKind.None, authority.GeometryKind);
        Assert.Empty(authority.Coordinates);
        Assert.Equal("Coast Guard", authority.AuthorityName);
    }

    [Fact]
    public void From_RelationshipsDataset_ResolvesTheAuthorityXlinks()
    {
        var dataset = S127Dataset.Open(GetTestDataPath("marine_relationships.gml"));

        var typed = S127MarineServicesDataset.From(dataset, out var diagnostics);

        // Two valid theAuthority bindings (pbp1 → auth1, vts1 → auth1)
        // plus one unresolved (ra1 → #missing).
        var unresolved = diagnostics.Where(d => d.Code == "xlink.unresolved").ToList();
        Assert.Single(unresolved);
        Assert.Equal("ra1", unresolved[0].RelatedId);
        Assert.Equal("theAuthority", unresolved[0].RelatedAttribute);

        var auth1 = Assert.Single(typed.Authorities);
        Assert.Equal("auth1", auth1.Id);
        Assert.Equal("Port Authority of New York", auth1.AuthorityName);

        var pbp = typed.Features.OfType<S127PilotBoardingPlace>().Single();
        Assert.NotNull(pbp.Authority);
        Assert.Same(auth1, pbp.Authority);

        var vts = typed.Features.OfType<S127VesselTrafficServiceArea>().Single();
        Assert.NotNull(vts.Authority);
        Assert.Same(auth1, vts.Authority);
        Assert.Equal(S127GeometryKind.Surface, vts.GeometryKind);

        // Restricted area with the unresolved theAuthority — Authority
        // property stays null but the typed object is still produced.
        var ra = typed.Features.OfType<S127RegulatedArea>().Single();
        Assert.Equal(S127RegulatedAreaKind.RestrictedArea, ra.Kind);
        Assert.Equal(14, ra.CategoryCode);
        Assert.Null(ra.Authority);

        // PilotService falls through to the catch-all and preserves its geometry.
        var other = Assert.Single(typed.OtherFeatures);
        Assert.Equal("PilotService", other.FeatureType);
        Assert.Equal(S127GeometryKind.Point, other.GeometryKind);
        Assert.Single(other.Coordinates);
    }

    [Fact]
    public void From_PreservesSourceAndProductMetadata()
    {
        var dataset = S127Dataset.Open(GetTestDataPath("marine_relationships.gml"));

        var typed = S127MarineServicesDataset.From(dataset, out _);

        Assert.Same(dataset, typed.Source);
        Assert.Equal("S-127", typed.ProductIdentifier);
        Assert.Equal("DS_S127_Relationships_Test", typed.DatasetIdentifier);
    }

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        var empty = new S127Dataset
        {
            ProductIdentifier = "S-127",
            DatasetIdentifier = "empty",
            Features = System.Collections.Immutable.ImmutableArray<S127Feature>.Empty,
            InformationTypes = System.Collections.Immutable.ImmutableArray<S127InformationType>.Empty,
        };

        Assert.Throws<InvalidOperationException>(() =>
            S127MarineServicesDataset.From(empty, out _));
    }

    [Fact]
    public void From_PreservesExtraAttributes_OnPilotBoardingPlace()
    {
        var dataset = S127Dataset.Open(GetTestDataPath("marine_mixed.gml"));

        var typed = S127MarineServicesDataset.From(dataset, out _);
        var pbp = typed.Features.OfType<S127PilotBoardingPlace>().Single();

        // ExtraAttributes excludes categoryOfPilotBoardingPlace (consumed)
        // but keeps everything else. The mixed fixture carries only that
        // one attribute, so ExtraAttributes is empty.
        Assert.DoesNotContain("categoryOfPilotBoardingPlace", pbp.ExtraAttributes.Keys);
    }
}
