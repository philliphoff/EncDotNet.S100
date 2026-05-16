using EncDotNet.S100.Mcp.Tools.Time;

namespace EncDotNet.S100.Mcp.Tools.Tests.Time;

public class TimeQueryTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T6 = new(2024, 1, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void At_normalises_offset_to_utc()
    {
        var pst = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(-8));
        var q = TimeQuery.At(pst);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 16, 0, 0, TimeSpan.Zero), q.Value);
        Assert.Equal(TimeSpan.Zero, q.Value.Offset);
    }

    [Fact]
    public void Between_rejects_reversed_window()
    {
        Assert.Throws<ArgumentException>(() => TimeQuery.Between(T6, T0));
    }

    [Fact]
    public void Between_accepts_zero_length_window()
    {
        var q = TimeQuery.Between(T0, T0);
        Assert.Equal(TimeSpan.Zero, q.Duration);
    }

    [Fact]
    public void Every_rejects_non_positive_step()
    {
        Assert.Throws<ArgumentException>(() => TimeQuery.Every(T0, T6, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => TimeQuery.Every(T0, T6, TimeSpan.FromMinutes(-30)));
    }

    [Fact]
    public void Series_enumerate_produces_expected_step_count()
    {
        var q = TimeQuery.Every(T0, T6, TimeSpan.FromHours(2));
        var instants = q.Enumerate();
        Assert.Equal(4, instants.Length); // 00, 02, 04, 06
        Assert.Equal(T0, instants[0]);
        Assert.Equal(T6, instants[^1]);
    }

    [Fact]
    public void Series_estimated_count_matches_enumeration()
    {
        var q = TimeQuery.Every(T0, T6, TimeSpan.FromHours(1));
        Assert.Equal(7, q.EstimatedCount);
        Assert.Equal(7, q.Enumerate().Length);
    }

    [Fact]
    public void Series_above_max_throws_on_enumerate()
    {
        // 1-second cadence over 6 hours = 21601 instants > MaxSeriesCount (4096).
        var q = TimeQuery.Every(T0, T6, TimeSpan.FromSeconds(1));
        Assert.True(q.EstimatedCount > TimeQuery.MaxSeriesCount);
        Assert.Throws<InvalidOperationException>(() => q.Enumerate());
    }

    [Fact]
    public void GetWindow_returns_degenerate_range_for_instant()
    {
        var q = TimeQuery.At(T0);
        var (from, to) = q.GetWindow();
        Assert.Equal(T0, from);
        Assert.Equal(T0, to);
    }

    [Fact]
    public void Overlaps_treats_null_validity_as_open_ended()
    {
        var q = TimeQuery.Between(T0, T6);
        Assert.True(q.Overlaps(null, null));
        Assert.True(q.Overlaps(null, T6));
        Assert.True(q.Overlaps(T0, null));
    }

    [Fact]
    public void Overlaps_returns_false_for_disjoint_window()
    {
        var q = TimeQuery.Between(T0, T6);
        var before = T0.AddHours(-2);
        Assert.False(q.Overlaps(before.AddHours(-1), before));
        var after = T6.AddHours(2);
        Assert.False(q.Overlaps(after, after.AddHours(1)));
    }

    [Fact]
    public void Overlaps_returns_true_for_touching_window()
    {
        var q = TimeQuery.Between(T0, T6);
        Assert.True(q.Overlaps(T6, T6.AddHours(2)));
        Assert.True(q.Overlaps(T0.AddHours(-2), T0));
    }

    [Fact]
    public void Contains_matches_endpoints_inclusively()
    {
        var q = TimeQuery.Between(T0, T6);
        Assert.True(q.Contains(T0));
        Assert.True(q.Contains(T6));
        Assert.True(q.Contains(T0.AddHours(3)));
        Assert.False(q.Contains(T0.AddSeconds(-1)));
        Assert.False(q.Contains(T6.AddSeconds(1)));
    }
}
