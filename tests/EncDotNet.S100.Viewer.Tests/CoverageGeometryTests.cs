using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

public class CoverageGeometryTests
{
    private static readonly NoaaEncProductCatalog Noaa = NoaaEncProductCatalogReader.Read(
        LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "noaa-enc-prodcat.xml"));

    private static CollectionItem NoaaItem(string name) =>
        NoaaEncFeedIndexer.Map(Noaa.Cells.Single(c => c.Name == name));

    private static CollectionItem Item(
        GeoBounds? bounds, int? band = null, int? minimumDisplayScale = null, int? maximumDisplayScale = null) => new()
        {
            Key = "k",
            ProductSpec = "S-57",
            Name = "k",
            Bounds = bounds,
            UsageBand = band,
            MinimumDisplayScale = minimumDisplayScale,
            MaximumDisplayScale = maximumDisplayScale,
            Location = NoItemLocation.Instance,
        };

    [Theory]
    [InlineData(-219.7, -216.9, new[] { 360.0 })]          // wholly west of −180 (NOAA Micronesia)
    [InlineData(170.0, 190.0, new[] { -360.0, 0.0 })]      // straddles +180: drawn on both sides
    [InlineData(-10.0, 10.0, new[] { 0.0 })]
    public void WorldShifts_bring_every_part_of_a_ring_into_the_world(double min, double max, double[] expected)
    {
        Assert.Equal(expected, CoverageGeometry.WorldShifts(min, max));
    }

    [Fact]
    public void Rings_west_of_the_antimeridian_project_inside_the_world()
    {
        var rings = CoverageGeometry.ToMercatorRings(NoaaItem("US3TC300"));

        Assert.Equal(2, rings.Count);
        Assert.All(rings.SelectMany(r => r), p => Assert.InRange(p.X, -20037509, 20037509));
    }

    [Fact]
    public void Bounds_only_items_draw_a_rectangle()
    {
        var ring = Assert.Single(CoverageGeometry.ToMercatorRings(Item(new GeoBounds(10, 20, 11, 21))));

        Assert.Equal(5, ring.Length);
        Assert.Empty(CoverageGeometry.ToMercatorRings(Item(null)));
    }

    [Theory]
    [InlineData(20_000_000, 1)]
    [InlineData(1_000_000, 2)]
    [InlineData(300_000, 3)]
    [InlineData(20_000, 5)]
    [InlineData(5_000, 6)]
    public void FinestEligibleBand_follows_scale(double scale, int band)
    {
        Assert.Equal(band, CoverageGeometry.FinestEligibleBand(scale));
    }

    [Fact]
    public void Band_items_show_in_a_two_band_window()
    {
        var bounds = new GeoBounds(0, 0, 1, 1);

        // At 1:1,000,000 bands 2 (suited) and 3 (preview) show; 1 and 4 do not.
        Assert.False(CoverageGeometry.IsVisibleAtScale(Item(bounds, band: 1), 1_000_000));
        Assert.True(CoverageGeometry.IsVisibleAtScale(Item(bounds, band: 2), 1_000_000));
        Assert.True(CoverageGeometry.IsVisibleAtScale(Item(bounds, band: 3), 1_000_000));
        Assert.False(CoverageGeometry.IsVisibleAtScale(Item(bounds, band: 4), 1_000_000));
    }

    [Fact]
    public void Non_band_items_show_when_zoomed_out_and_hide_well_past_their_finest_scale()
    {
        // An S-101 approach dataset (1:180,000 – 1:90,000).
        var item = Item(new GeoBounds(0, 0, 1, 1), minimumDisplayScale: 180_000, maximumDisplayScale: 90_000);

        Assert.True(CoverageGeometry.IsVisibleAtScale(item, 50_000_000));  // zoomed far out: still outlined
        Assert.True(CoverageGeometry.IsVisibleAtScale(item, 1_000_000));
        Assert.True(CoverageGeometry.IsVisibleAtScale(item, 90_000));
        Assert.True(CoverageGeometry.IsVisibleAtScale(item, 22_500));      // 4× past its finest
        Assert.False(CoverageGeometry.IsVisibleAtScale(item, 10_000));     // harbour detail: dropped
        Assert.True(CoverageGeometry.IsVisibleAtScale(Item(new GeoBounds(0, 0, 1, 1)), 1_000));
    }
}
