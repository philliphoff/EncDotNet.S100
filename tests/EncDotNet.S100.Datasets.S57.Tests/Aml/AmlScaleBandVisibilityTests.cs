using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S57.Tests.Aml;

public class AmlScaleBandVisibilityTests
{
    // A box from (west, south) to (east, north).
    private static IReadOnlyList<CoverageArea> Box(double west, double south, double east, double north) =>
    [
        new CoverageArea
        {
            ExteriorRing =
            [
                new GeoPosition(south, west),
                new GeoPosition(south, east),
                new GeoPosition(north, east),
                new GeoPosition(north, west),
                new GeoPosition(south, west),
            ],
        },
    ];

    private static readonly IReadOnlyList<CoverageArea> Wide = Box(0, 50, 2, 51);

    [Theory]
    [InlineData(5, 625_000)]
    [InlineData(6, 125_000)]
    [InlineData(7, 25_000)]
    [InlineData(9, 1_500)]
    public void A_cell_shows_up_to_its_bands_coarsest_display_scale(int band, int limit)
    {
        var window = AmlScaleBandVisibility.Resolve([new AmlBandCell("CLB", band, Wide)])[0];

        Assert.Equal(limit, window.ZoomOutLimit);
        Assert.True(window.ShowsAt(limit));
        Assert.False(window.ShowsAt(limit * 1.01));
        // Zoomed in past the band, with nothing finer, it keeps showing.
        Assert.True(window.ShowsAt(500));
    }

    [Fact]
    public void Band_zero_and_cells_without_a_band_show_at_every_scale()
    {
        var windows = AmlScaleBandVisibility.Resolve(
        [
            new AmlBandCell("RAL", 0, Wide),
            new AmlBandCell("RAL", null, Wide),
            new AmlBandCell("RAL", 9, Wide),
        ]);

        Assert.Equal(AmlBandWindow.Always, windows[0]);
        Assert.Equal(AmlBandWindow.Always, windows[1]);
        Assert.True(windows[0].ShowsAt(400_000_000));
    }

    [Fact]
    public void The_finer_band_wins_where_its_cells_cover_a_coarser_cell()
    {
        // Two band-6 cells together cover the band-5 cell.
        var windows = AmlScaleBandVisibility.Resolve(
        [
            new AmlBandCell("CLB", 5, Wide),
            new AmlBandCell("CLB", 6, Box(0, 50, 1, 51)),
            new AmlBandCell("CLB", 6, Box(1, 50, 2, 51)),
        ]);

        var coarse = windows[0];
        Assert.Equal(125_000, coarse.CoveredAtOrBelow);
        // 1:625 000 – 1:125 001: only band 5 is in its range.
        Assert.True(coarse.ShowsAt(300_000));
        Assert.False(windows[1].ShowsAt(300_000));
        // 1:125 000 and finer: band 6 shows and wins.
        Assert.False(coarse.ShowsAt(110_000));
        Assert.True(windows[1].ShowsAt(110_000));
        Assert.True(windows[2].ShowsAt(110_000));
    }

    [Fact]
    public void A_coarser_cell_only_partly_covered_by_finer_cells_keeps_showing()
    {
        var windows = AmlScaleBandVisibility.Resolve(
        [
            new AmlBandCell("CLB", 4, Wide),
            new AmlBandCell("CLB", 6, Box(0, 50, 1, 51)),
        ]);

        Assert.Null(windows[0].CoveredAtOrBelow);
        Assert.True(windows[0].ShowsAt(50_000));
    }

    [Fact]
    public void Bands_only_compete_within_a_product()
    {
        var windows = AmlScaleBandVisibility.Resolve(
        [
            new AmlBandCell("RAL", 5, Wide),
            new AmlBandCell("SBO", 7, Wide),
        ]);

        Assert.Null(windows[0].CoveredAtOrBelow);
        Assert.True(windows[0].ShowsAt(20_000));
    }

    [Fact]
    public void The_cover_completes_at_the_finer_limit_of_the_last_cell_it_needs()
    {
        // Band 6 covers the west half from 1:125 000, band 7 the east half
        // from 1:25 000: the band-5 cell gives way from 1:25 000.
        var windows = AmlScaleBandVisibility.Resolve(
        [
            new AmlBandCell("CLB", 5, Wide),
            new AmlBandCell("CLB", 6, Box(0, 50, 1, 51)),
            new AmlBandCell("CLB", 7, Box(1, 50, 2, 51)),
        ]);

        Assert.Equal(25_000, windows[0].CoveredAtOrBelow);
        Assert.True(windows[0].ShowsAt(50_000));
        Assert.False(windows[0].ShowsAt(20_000));
    }

    [Fact]
    public void An_unknown_scale_shows_everything()
    {
        var window = new AmlBandWindow(125_000, 25_000);

        Assert.True(window.ShowsAt(double.NaN));
        Assert.True(window.ShowsAt(0));
    }
}
