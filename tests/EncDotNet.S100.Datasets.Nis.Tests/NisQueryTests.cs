using EncDotNet.S100.Datasets.Nis.Tests.TestSupport;

namespace EncDotNet.S100.Datasets.Nis.Tests;

/// <summary>Tests for <see cref="NisQuery"/> and <see cref="InMemoryNisSource"/>.</summary>
public sealed class NisQueryTests
{
    [Fact]
    public void Filters_by_cell_bounds_and_deletion()
    {
        var inside = Make.Feature("LNDMRK", Make.Point(1, 1), cell: "XX5AAAAA.000");
        var outside = Make.Feature("LNDMRK", Make.Point(5, 5), cell: "XX5AAAAA.000");
        var other = Make.Feature("LNDMRK", Make.Point(1, 1), cell: "XX5BBBBB.000");
        var collection = Make.Feature("C_AGGR", null, cell: "XX5AAAAA.000");
        var deleted = new NisFeature { FeatureClass = "Test", ObjectClass = "LNDMRK", DatasetName = "XX5AAAAA.000", IsDeleted = true, Geometry = Make.Point(1, 1) };
        var source = new InMemoryNisSource([inside, outside, other, collection, deleted]);

        var features = source.GetFeatures(new NisQuery { DatasetName = "xx5aaaaa.000", Bounds = new NisBounds(0, 0, 2, 2) }).ToList();

        Assert.Equal([inside, collection], features);
        Assert.Equal(["XX5AAAAA.000", "XX5BBBBB.000"], source.GetDatasetNames());
        Assert.Equal(5, source.GetFeatures(new NisQuery { IncludeDeleted = true }).Count());
    }

    [Fact]
    public void Bounds_overlap_a_geometry_that_surrounds_them()
    {
        var bounds = new NisBounds(1, 1, 2, 2);
        Assert.True(bounds.Intersects(Make.Area(Make.Square(0, 0, 3))));
        Assert.False(bounds.Intersects(Make.Area(Make.Square(3, 3, 1))));
    }
}
