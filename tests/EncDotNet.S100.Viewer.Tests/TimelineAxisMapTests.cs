using System;
using System.Collections.Generic;
using EncDotNet.S100.Viewer.Services;
using Xunit;

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
        // Two 2h clusters with a 6h gap. Linearly the gap would be 60% of the
        // axis; collapsed it must be far smaller and the clusters far larger.
        var map = Map(T0, T0.AddHours(10),
            (T0, T0.AddHours(2)),
            (T0.AddHours(8), T0.AddHours(10)));

        Assert.Equal(2, map.CoverageBands.Count);
        double clusterWidth = map.CoverageBands[0].Width;
        double gap = map.CoverageBands[1].Start - (map.CoverageBands[0].Start + map.CoverageBands[0].Width);

        Assert.True(clusterWidth > 0.4, $"cluster width was {clusterWidth}");
        Assert.True(gap is > 0.0 and < 0.2, $"gap was {gap}");
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
