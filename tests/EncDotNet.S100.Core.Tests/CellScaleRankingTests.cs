using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Core.Tests;

public class CellScaleRankingTests
{
    [Theory]
    [InlineData(12_000, 22_000, null, 12_000)]
    [InlineData(null, 22_000, null, 22_000)]
    [InlineData(0, 22_000, null, 22_000)]
    [InlineData(null, null, null, null)]
    [InlineData(12_000, 22_000, 3_500, 12_000)]
    public void Rank_prefers_the_compilation_scale(int? compilation, int? minimumDisplay, int? maximumDisplay, int? expected) =>
        Assert.Equal(expected, CellScaleRanking.Rank(compilation, minimumDisplay, maximumDisplay)?.Scale);

    [Theory]
    [InlineData(null, 22_000, 3_500, 3_500)]
    [InlineData(12_000, 22_000, 3_500, null)]
    [InlineData(null, 22_000, 22_000, null)]
    [InlineData(null, 22_000, 45_000, null)]
    [InlineData(null, 22_000, 0, null)]
    public void Tie_break_is_a_finer_maximum_display_scale_of_a_cell_ranked_by_its_window(
        int? compilation, int? minimumDisplay, int? maximumDisplay, int? expected) =>
        Assert.Equal(expected, CellScaleRanking.Rank(compilation, minimumDisplay, maximumDisplay)?.TieBreak);

    [Fact]
    public void Only_a_strictly_finer_rank_is_finer_and_unranked_is_coarsest()
    {
        Assert.True(CellScaleRanking.IsFiner(new CellRank(22_000), new CellRank(90_000)));
        Assert.False(CellScaleRanking.IsFiner(new CellRank(22_000), new CellRank(22_000)));
        Assert.False(CellScaleRanking.IsFiner(new CellRank(90_000), new CellRank(22_000)));
        Assert.True(CellScaleRanking.IsFiner(new CellRank(90_000), null));
        Assert.False(CellScaleRanking.IsFiner(null, new CellRank(90_000)));
        Assert.False(CellScaleRanking.IsFiner(null, null));
    }

    [Fact]
    public void Same_minimum_display_scale_ranks_by_the_finer_maximum()
    {
        // IC-ENC Cowes: berthing cell 101GB00602793 (1:22 000 to 1:3 500) over
        // harbour cell 101GB00502038 (1:22 000 to 1:12 000).
        var berthing = CellScaleRanking.Rank(null, 22_000, 3_500);
        var harbour = CellScaleRanking.Rank(null, 22_000, 12_000);
        var noMaximum = CellScaleRanking.Rank(null, 22_000);

        Assert.True(CellScaleRanking.IsFiner(berthing, harbour));
        Assert.False(CellScaleRanking.IsFiner(harbour, berthing));
        Assert.True(CellScaleRanking.IsFiner(harbour, noMaximum));
        Assert.False(CellScaleRanking.IsFiner(noMaximum, harbour));
        Assert.False(CellScaleRanking.IsFiner(berthing, CellScaleRanking.Rank(null, 22_000, 3_500)));

        // The scale still decides first: a coarser window never wins on its maximum.
        Assert.True(CellScaleRanking.IsFiner(harbour, CellScaleRanking.Rank(null, 45_000, 1_000)));
    }

    [Fact]
    public void Finest_band_keeps_every_cell_at_the_finest_rank_in_order()
    {
        var cells = new (string Name, CellRank? Rank)[]
        {
            ("coastal", new CellRank(90_000)), ("harbourA", new CellRank(22_000)), ("none", null), ("harbourB", new CellRank(22_000)),
        };

        Assert.Equal(["harbourA", "harbourB"], CellScaleRanking.FinestBand(cells, c => c.Rank).Select(c => c.Name));
        Assert.Equal(["a", "b"], CellScaleRanking.FinestBand(new (string Name, CellRank? Rank)[] { ("a", null), ("b", null) }, c => c.Rank).Select(c => c.Name));
        Assert.Empty(CellScaleRanking.FinestBand(Array.Empty<CellRank?>(), c => c));

        var tied = new (string Name, CellRank? Rank)[]
        {
            ("harbour", new CellRank(22_000, 12_000)), ("berthing", new CellRank(22_000, 3_500)), ("plain", new CellRank(22_000)),
        };
        Assert.Equal(["berthing"], CellScaleRanking.FinestBand(tied, c => c.Rank).Select(c => c.Name));
    }

    [Fact]
    public void Coverage_contains_honours_holes()
    {
        var area = new CoverageArea
        {
            ExteriorRing = [new(0, 0), new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
            InteriorRings = [[new(0.4, 0.4), new(0.4, 0.6), new(0.6, 0.6), new(0.6, 0.4), new(0.4, 0.4)]],
        };

        Assert.True(area.Contains(0.2, 0.2));
        Assert.False(area.Contains(0.5, 0.5));
        Assert.False(area.Contains(1.5, 0.5));
    }
}
