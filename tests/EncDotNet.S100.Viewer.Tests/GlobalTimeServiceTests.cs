using System;
using System.Collections.Generic;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class GlobalTimeServiceTests
{
    private sealed class StubTimeAware : ITimeAwareDataset
    {
        public IReadOnlyList<DateTime> AvailableTimes { get; }
        public DateTime? CurrentTime { get; set; }
        public StubTimeAware(params DateTime[] times) { AvailableTimes = times; }
        public DateTime? SnapTo(DateTime t)
        {
            DateTime? best = null;
            var bestDelta = TimeSpan.MaxValue;
            foreach (var s in AvailableTimes)
            {
                var d = (s - t).Duration();
                if (d < bestDelta) { bestDelta = d; best = s; }
            }
            return best;
        }
    }

    private static DatasetEntry NewEntry() => new("/tmp/d", "S104");

    [Fact]
    public void Empty_service_is_inactive()
    {
        var s = new GlobalTimeService();
        Assert.False(s.IsActive);
        Assert.Null(s.MinTime);
        Assert.Null(s.MaxTime);
    }

    [Fact]
    public void Register_aggregates_min_max_across_datasets()
    {
        var s = new GlobalTimeService();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        s.Register(NewEntry(), new StubTimeAware(t1, t2));
        s.Register(NewEntry(), new StubTimeAware(t2, t3));

        Assert.True(s.IsActive);
        Assert.Equal(t1, s.MinTime);
        Assert.Equal(t3, s.MaxTime);
        Assert.Equal(3, s.AllSamples.Count);
    }

    [Fact]
    public void SetCurrentTime_clamps_to_range_and_raises_event()
    {
        var s = new GlobalTimeService();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        s.Register(NewEntry(), new StubTimeAware(t1, t2));
        // After Register, CurrentTime auto-initialises to MinTime (t1).
        Assert.Equal(t1, s.CurrentTime);

        DateTime? observed = null;
        s.CurrentTimeChanged += t => observed = t;

        s.SetCurrentTime(t2.AddHours(99)); // clamps to t2
        Assert.Equal(t2, observed);
        Assert.Equal(t2, s.CurrentTime);

        s.SetCurrentTime(t1.AddHours(-1)); // clamps to t1
        Assert.Equal(t1, observed);
    }

    [Fact]
    public void Unregister_recomputes_range()
    {
        var s = new GlobalTimeService();
        var entry1 = NewEntry();
        var entry2 = NewEntry();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Register(entry1, new StubTimeAware(t1));
        s.Register(entry2, new StubTimeAware(t2));

        s.Unregister(entry2);

        Assert.Equal(t1, s.MaxTime);
        Assert.Single(s.AllSamples);
    }
}
