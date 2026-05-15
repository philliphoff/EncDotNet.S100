using System.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S125.DataModel;

namespace EncDotNet.S100.Datasets.S125.Tests;

public class DataModelTests
{
    private static string GetTestDataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", filename);

    [Fact]
    public void From_PointDataset_ProjectsAidsAndResolvesStatus()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_point.gml"));

        var typed = S125AtonDataset.From(dataset, out var diagnostics);

        Assert.Equal("S-125", typed.ProductIdentifier);
        Assert.Equal(2, typed.Aids.Length);
        Assert.Single(typed.StatusInformation);
        Assert.Empty(diagnostics);

        var buoy = Assert.IsType<S125Buoy>(typed.Aids.Single(a => a is S125Buoy));
        Assert.Equal(S125BuoyKind.Lateral, buoy.Kind);
        Assert.NotNull(buoy.Position);
        Assert.Equal(36.95, buoy.Position!.Value.Latitude, 4);

        // xlink-resolved status — the headline value-add.
        Assert.NotNull(buoy.Status);
        Assert.Equal(S125ChangeType.AdvanceNoticeOfChange, buoy.Status!.ChangeType);
        Assert.True(buoy.Status.IsOperational);
        Assert.NotNull(buoy.Status.FixedDateRange);

        var landmark = Assert.IsType<S125Structure>(typed.Aids.Single(a => a is S125Structure));
        Assert.Equal(S125StructureKind.Landmark, landmark.Kind);
        Assert.Null(landmark.Status);
    }

    [Fact]
    public void From_RelationshipsDataset_ResolvesHostStructure()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_relationships.gml"));

        var typed = S125AtonDataset.From(dataset, out _);

        var light = typed.Aids.OfType<S125Light>().Single();
        Assert.NotNull(light.HostStructure);
        Assert.Equal("beacon1", light.HostStructure!.Id);
        Assert.IsType<S125Beacon>(light.HostStructure);
    }

    [Fact]
    public void From_RelationshipsDataset_DistinguishesVirtualAndPhysicalAis()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_relationships.gml"));

        var typed = S125AtonDataset.From(dataset, out _);

        var ais = typed.Aids.OfType<S125AisAton>().ToList();
        Assert.Equal(2, ais.Count);

        var virt = ais.Single(a => a.IsVirtual);
        Assert.Equal(S125AisKind.Virtual, virt.Kind);
        Assert.Equal("vais1", virt.Id);

        var phys = ais.Single(a => !a.IsVirtual);
        Assert.Equal(S125AisKind.Physical, phys.Kind);
    }

    [Fact]
    public void From_RelationshipsDataset_ResolvesDiscrepancyStatusAsNonOperational()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_relationships.gml"));

        var typed = S125AtonDataset.From(dataset, out _);

        var light = typed.Aids.OfType<S125Light>().Single();
        Assert.NotNull(light.Status);
        Assert.Equal(S125ChangeType.Discrepancy, light.Status!.ChangeType);
        Assert.False(light.Status.IsOperational);
        Assert.Equal("Light reported unreliable", light.Status.ChangeDetails);
    }

    [Fact]
    public void From_RelationshipsDataset_RoundTripsUnknownAttributeViaExtraAttributes()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_relationships.gml"));

        var typed = S125AtonDataset.From(dataset, out _);

        var beacon = typed.Aids.OfType<S125Beacon>().Single();
        Assert.True(beacon.ExtraAttributes.ContainsKey("customAttribute"));
        Assert.Equal("extension-value", beacon.ExtraAttributes["customAttribute"]);
    }

    [Fact]
    public void From_RelationshipsDataset_ResolvesAggregationMembers()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_relationships.gml"));

        var typed = S125AtonDataset.From(dataset, out _);

        var agg = typed.Aggregations.Single();
        Assert.Equal(S125AggregationKind.Aggregation, agg.Kind);
        Assert.Equal(3, agg.Members.Length);
        Assert.Contains(agg.Members, m => m.Id == "beacon1");
        Assert.Contains(agg.Members, m => m.Id == "light1");
        Assert.Contains(agg.Members, m => m.Id == "vais1");
    }

    [Fact]
    public void From_RelationshipsDataset_EmitsParseFailureDiagnostic()
    {
        var dataset = S125Dataset.Open(GetTestDataPath("aton_relationships.gml"));

        S125AtonDataset.From(dataset, out var diagnostics);

        // categoryOfAggregation="not-a-number" should produce a parse warning.
        Assert.Contains(diagnostics, d =>
            d.Code == "attribute.parse.int" &&
            d.RelatedAttribute == "categoryOfAggregation");
    }

    [Fact]
    public void From_UnresolvedStatusXlink_EmitsDiagnosticDoesNotThrow()
    {
        var dataset = new S125Dataset
        {
            ProductIdentifier = "S-125",
            DatasetIdentifier = "dangling",
            Features = System.Collections.Immutable.ImmutableArray.Create(new S125Feature
            {
                Id = "buoyX",
                FeatureType = "LateralBuoy",
                GeometryType = EncDotNet.S100.Gml.GmlGeometryType.None,
                Attributes = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty,
                ComplexAttributes = System.Collections.Immutable.ImmutableArray<S125ComplexAttribute>.Empty,
                InformationReferences = System.Collections.Immutable.ImmutableArray.Create(
                    new S125InformationReference { Role = "AtoNStatus", InformationRef = "missing" }),
            }),
            InformationTypes = System.Collections.Immutable.ImmutableArray<S125InformationType>.Empty,
        };

        var typed = S125AtonDataset.From(dataset, out var diagnostics);

        var buoy = typed.Aids.OfType<S125Buoy>().Single();
        Assert.Null(buoy.Status);
        Assert.Contains(diagnostics, d => d.Code == "xlink.unresolved");
    }

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        var empty = new S125Dataset
        {
            ProductIdentifier = "S-125",
            Features = System.Collections.Immutable.ImmutableArray<S125Feature>.Empty,
            InformationTypes = System.Collections.Immutable.ImmutableArray<S125InformationType>.Empty,
        };

        Assert.Throws<InvalidOperationException>(() => S125AtonDataset.From(empty, out _));
    }
}
