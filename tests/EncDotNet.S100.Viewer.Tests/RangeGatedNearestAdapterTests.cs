using System;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="TimeAwareDatasetAdapter.RangeGatedNearestAdapter"/>,
/// which gates S-111 datasets to their covered forecast window so that
/// out-of-range files are hidden rather than drawn with stale,
/// endpoint-clamped arrows.
/// </summary>
public sealed class RangeGatedNearestAdapterTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime[] Steps(int count, int intervalMinutes)
    {
        var times = new DateTime[count];
        for (int i = 0; i < count; i++)
            times[i] = Base.AddMinutes(i * intervalMinutes);
        return times;
    }

    [Fact]
    public void InsideRange_SnapsToNearestSample()
    {
        var times = Steps(5, 20); // 00:00 .. 01:20 every 20 min
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(times, () => null);

        // 00:38 is nearest to 00:40 (index 2).
        var snapped = adapter.SnapTo(Base.AddMinutes(38));
        Assert.Equal(Base.AddMinutes(40), snapped);
    }

    [Fact]
    public void BeyondRangePlusTolerance_ReturnsNull()
    {
        var times = Steps(5, 20); // last = 01:20, tolerance = 20 min
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(times, () => null);

        // 02:00 is 40 min past the last sample (> 20 min tolerance).
        Assert.Null(adapter.SnapTo(Base.AddMinutes(120)));
    }

    [Fact]
    public void BeforeRangeMinusTolerance_ReturnsNull()
    {
        var times = Steps(5, 20);
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(times, () => null);

        // 30 min before first sample (> 20 min tolerance).
        Assert.Null(adapter.SnapTo(Base.AddMinutes(-30)));
    }

    [Fact]
    public void WithinTolerance_StaysVisible()
    {
        var times = Steps(5, 20);
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(times, () => null);

        // 10 min past the last sample (within 20 min tolerance) snaps to last.
        var snapped = adapter.SnapTo(Base.AddMinutes(90));
        Assert.Equal(Base.AddMinutes(80), snapped);
    }

    [Fact]
    public void SingleSample_NotGated()
    {
        var times = new[] { Base };
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(times, () => null);

        // No gating for single-sample datasets: always returns the sample.
        Assert.Equal(Base, adapter.SnapTo(Base.AddDays(100)));
    }

    [Fact]
    public void Empty_ReturnsNull()
    {
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(Array.Empty<DateTime>(), () => null);
        Assert.Null(adapter.SnapTo(Base));
    }

    [Fact]
    public void CoverageIntervals_PadByOneSampleInterval()
    {
        var times = Steps(5, 20); // 00:00 .. 01:20, interval 20 min
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(times, () => null);

        var intervals = adapter.CoverageIntervals;
        Assert.Single(intervals);
        // Padded by one 20-min interval at each end.
        Assert.Equal(Base.AddMinutes(-20), intervals[0].Start);
        Assert.Equal(Base.AddMinutes(80 + 20), intervals[0].End);
    }

    [Fact]
    public void CoverageIntervals_SingleSample_IsPoint()
    {
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(new[] { Base }, () => null);

        var intervals = adapter.CoverageIntervals;
        Assert.Single(intervals);
        Assert.Equal(Base, intervals[0].Start);
        Assert.Equal(Base, intervals[0].End);
    }

    [Fact]
    public void CoverageIntervals_Empty_IsEmpty()
    {
        var adapter = new TimeAwareDatasetAdapter.RangeGatedNearestAdapter(Array.Empty<DateTime>(), () => null);
        Assert.Empty(adapter.CoverageIntervals);
    }
}
