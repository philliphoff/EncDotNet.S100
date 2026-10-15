using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Core.Tests;

public class CellScaleRankingTests
{
    [Theory]
    [InlineData(12_000, 22_000, 12_000)]
    [InlineData(null, 22_000, 22_000)]
    [InlineData(0, 22_000, 22_000)]
    [InlineData(null, null, null)]
    public void Ranking_scale_prefers_the_compilation_scale(int? compilation, int? minimumDisplay, int? expected) =>
        Assert.Equal(expected, CellScaleRanking.RankingScale(compilation, minimumDisplay));

    [Fact]
    public void Only_a_strictly_smaller_denominator_is_finer_and_unranked_is_coarsest()
    {
        Assert.True(CellScaleRanking.IsFiner(22_000, 90_000));
        Assert.False(CellScaleRanking.IsFiner(22_000, 22_000));
        Assert.False(CellScaleRanking.IsFiner(90_000, 22_000));
        Assert.True(CellScaleRanking.IsFiner(90_000, null));
        Assert.False(CellScaleRanking.IsFiner(null, 90_000));
        Assert.False(CellScaleRanking.IsFiner(null, null));
    }

    [Fact]
    public void Finest_band_keeps_every_cell_at_the_finest_scale_in_order()
    {
        var cells = new (string Name, int? Rank)[] { ("coastal", 90_000), ("harbourA", 22_000), ("none", null), ("harbourB", 22_000) };

        Assert.Equal(["harbourA", "harbourB"], CellScaleRanking.FinestBand(cells, c => c.Rank).Select(c => c.Name));
        Assert.Equal(["a", "b"], CellScaleRanking.FinestBand(new (string Name, int? Rank)[] { ("a", null), ("b", null) }, c => c.Rank).Select(c => c.Name));
        Assert.Empty(CellScaleRanking.FinestBand(Array.Empty<int?>(), c => c));
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
