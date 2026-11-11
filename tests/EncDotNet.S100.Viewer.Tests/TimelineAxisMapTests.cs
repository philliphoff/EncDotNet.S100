using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="TimelineAxisMap"/>, the gap-collapsing
/// (focus+context) mapping between wall-clock time and the normalized
/// <c>[0,1]</c> slider position.
/// </summary>
public sealed class TimelineAxisMapTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TimelineAxisMap Map(DateTime min, DateTime max, params (DateTime, DateTime)[] segs)
    {
        var list = new List<CoverageSegment>();
        foreach (var (s, e) in segs) list.Add(new CoverageSegment(s, e));
        return new TimelineAxisMap(min, max, list);
    }

    [Fact]
    public void Contiguous_range_maps_linearly()
    {
        // Single coverage segment spanning the whole range => identity.
        var map = Map(T0, T0.AddHours(10), (T0, T0.AddHours(10)));

        Assert.Equal(0.0, map.ToPosition(T0), 6);
        Assert.Equal(0.5, map.ToPosition(T0.AddHours(5)), 6);
        Assert.Equal(1.0, map.ToPosition(T0.AddHours(10)), 6);
        Assert.Single(map.CoverageBands);
        Assert.Equal(0.0, map.CoverageBands[0].Start, 6);
        Assert.Equal(1.0, map.CoverageBands[0].Width, 6);
    }

    [Fact]
    public void Degenerate_range_collapses_to_a_point()
    {
        var map = Map(T0, T0);
        Assert.True(map.IsDegenerate);
        Assert.Equal(0.0, map.ToPosition(T0.AddHours(3)), 6);
        Assert.Equal(T0, map.ToTime(0.7));
        Assert.Empty(map.CoverageBands);
    }

    [Fact]
    public void Gap_is_compressed_so_clusters_expand()
    {
        // Two 2h clusters with a 30h gap. Linearly the gap would be 88% of the
        // axis; collapsed it gets a log-scaled width and the clusters expand.
        var map = Map(T0, T0.AddHours(34),
            (T0, T0.AddHours(2)),
            (T0.AddHours(32), T0.AddHours(34)));

        Assert.Equal(2, map.CoverageBands.Count);
        var gap = Assert.Single(map.Gaps);
        Assert.Equal(TimelineAxisMap.GapWidth(TimeSpan.FromHours(30)), gap.Width, 6);
        Assert.Equal((1 - gap.Width) / 2, map.CoverageBands[0].Width, 6);
        Assert.Equal(TimeSpan.FromHours(30), gap.Length);
    }

    [Fact]
    public void Rotterdam_gaps_widen_with_their_length_within_the_limits()
    {
        // Four week-long clusters separated by gaps of 6, 8, 10 and 12 weeks
        // (the last one up to a clusterless stretch before now).
        var week = TimeSpan.FromDays(7);
        var starts = new[] { T0, T0 + 7 * week, T0 + 16 * week, T0 + 27 * week };
        var segments = starts.Select(s => (s, s + week)).ToArray();
        var map = Map(T0, starts[^1] + 13 * week, segments);

        Assert.Equal(4, map.CoverageBands.Count);
        Assert.Equal(4, map.Gaps.Count);
        var widths = map.Gaps.Select(g => g.Width).ToArray();
        Assert.All(widths, w => Assert.InRange(w, TimelineAxisMap.MinimumGapWidth, TimelineAxisMap.MaximumGapWidth));
        Assert.Equal(widths.OrderBy(w => w), widths);
        Assert.True(widths.Zip(widths.Skip(1)).All(p => p.Second > p.First), "widths increase with duration");
    }

    [Fact]
    public void A_short_gap_is_drawn_to_scale()
    {
        // A 3h gap in an 80h range is under max(6 h, 15 % of 77 h): not collapsed.
        var map = Map(T0, T0.AddHours(80), (T0, T0.AddHours(40)), (T0.AddHours(43), T0.AddHours(80)));

        Assert.Empty(map.Gaps);
        Assert.Equal(40 / 80.0, map.CoverageBands[0].Width, 6);
        Assert.Equal(43 / 80.0, map.CoverageBands[1].Start, 6);
    }

    [Fact]
    public void A_focus_time_in_a_gap_keeps_a_stretch_to_scale()
    {
        var now = T0.AddDays(60);
        var segments = new List<CoverageSegment> { new(T0, T0.AddDays(2)), new(T0.AddDays(100), T0.AddDays(102)) };

        var map = new TimelineAxisMap(T0, T0.AddDays(102), segments, [now]);

        Assert.False(map.IsInCollapsedGap(now));
        Assert.Equal(2, map.Gaps.Count);
        Assert.True(map.Gaps[0].To <= now && map.Gaps[1].From >= now);
        // The stretch around now is to scale: an hour either side is a measurable, symmetric distance.
        var before = map.ToPosition(now.AddHours(-1));
        var after = map.ToPosition(now.AddHours(1));
        Assert.True(after - before > 0);
        Assert.Equal(map.ToPosition(now) - before, after - map.ToPosition(now), 9);
    }

    [Fact]
    public void ToPosition_and_ToTime_round_trip_within_data()
    {
        var map = Map(T0, T0.AddHours(10),
            (T0, T0.AddHours(2)),
            (T0.AddHours(8), T0.AddHours(10)));

        foreach (var t in new[] { T0, T0.AddHours(1), T0.AddHours(2), T0.AddHours(8), T0.AddHours(9), T0.AddHours(10) })
        {
            var pos = map.ToPosition(t);
            var back = map.ToTime(pos);
            Assert.True((back - t).Duration() < TimeSpan.FromSeconds(1), $"{t:O} -> {pos} -> {back:O}");
        }
    }

    [Fact]
    public void ToPosition_is_monotonic_non_decreasing()
    {
        var map = Map(T0, T0.AddHours(10),
            (T0, T0.AddHours(2)),
            (T0.AddHours(8), T0.AddHours(10)));

        double prev = -1;
        for (int h = 0; h <= 10; h++)
        {
            double p = map.ToPosition(T0.AddHours(h));
            Assert.True(p >= prev, $"position decreased at hour {h}: {p} < {prev}");
            Assert.InRange(p, 0.0, 1.0);
            prev = p;
        }
    }

    [Fact]
    public void Out_of_range_inputs_are_clamped()
    {
        var map = Map(T0, T0.AddHours(10), (T0, T0.AddHours(10)));
        Assert.Equal(0.0, map.ToPosition(T0.AddHours(-5)), 6);
        Assert.Equal(1.0, map.ToPosition(T0.AddHours(20)), 6);
        Assert.Equal(T0, map.ToTime(-1));
        Assert.Equal(T0.AddHours(10), map.ToTime(2));
    }
}
