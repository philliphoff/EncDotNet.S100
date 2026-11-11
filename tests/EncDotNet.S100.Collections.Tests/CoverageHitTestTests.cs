using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Tests;

public class CoverageHitTestTests
{
    private static readonly NoaaEncProductCatalog Noaa = NoaaEncProductCatalogReader.Read(TestPaths.Fixture("noaa-enc-prodcat.xml"));

    private static CollectionItem NoaaItem(string name) =>
        NoaaEncFeedIndexer.Map(Noaa.Cells.Single(c => c.Name == name));

    private static CollectionItem Item(GeoBounds? bounds, int? band = null) => new()
    {
        Key = "k",
        ProductSpec = "S-57",
        Name = "k",
        Bounds = bounds,
        UsageBand = band,
        Location = NoItemLocation.Instance,
    };

    [Fact]
    public void Unwrap_makes_longitudes_continuous_across_the_antimeridian()
    {
        var ring = CoverageHitTest.Unwrap([new(50, 179), new(51, -179), new(52, -178)]);

        Assert.Equal([179.0, 181.0, 182.0], ring.Select(p => p.Longitude));
    }

    [Fact]
    public void Contains_handles_continuous_longitudes_and_holes()
    {
        // US3TC300 is published at −219.5 (140.5°E).
        Assert.True(CoverageHitTest.Contains(NoaaItem("US3TC300"), new GeoPosition(8.1, 140.4)));
        Assert.False(CoverageHitTest.Contains(NoaaItem("US3TC300"), new GeoPosition(7.5, 140.4)));

        var michigan = NoaaItem("US5MI62M");
        Assert.True(CoverageHitTest.Contains(michigan, new GeoPosition(46.2, -84.0)));
        Assert.False(CoverageHitTest.Contains(michigan, new GeoPosition(46.315, -83.99)));  // inside the hole
    }

    [Fact]
    public void Bounds_only_items_contain_points_inside_their_bounds()
    {
        var item = Item(new GeoBounds(10, 20, 11, 21));

        Assert.True(CoverageHitTest.Contains(item, new GeoPosition(10.5, 20.5)));
        Assert.False(CoverageHitTest.Contains(item, new GeoPosition(12, 20.5)));
        Assert.False(CoverageHitTest.Contains(Item(null), new GeoPosition(10.5, 20.5)));
    }

    [Fact]
    public void Area_prefers_smaller_bounds_and_ranks_unbounded_items_last()
    {
        Assert.True(CoverageHitTest.Area(Item(new GeoBounds(10, 20, 10.5, 20.5))) < CoverageHitTest.Area(Item(new GeoBounds(10, 20, 11, 21))));
        Assert.Equal(double.MaxValue, CoverageHitTest.Area(Item(null)));
    }
}
