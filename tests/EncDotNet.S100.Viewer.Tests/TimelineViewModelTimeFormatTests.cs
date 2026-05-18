using System;
using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class TimelineViewModelTimeFormatTests
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

    private sealed class StubTimeFormat : ITimeFormatProvider
    {
        public TimeFormat Current { get; private set; }
        public StubTimeFormat(TimeFormat initial) => Current = initial;
        public event Action<TimeFormat>? TimeFormatChanged;
        public void Set(TimeFormat v) { Current = v; TimeFormatChanged?.Invoke(v); }
    }

    private static DatasetEntry NewEntry() => new("/tmp/d", "S104");

    private static GlobalTimeService WithSamples()
    {
        var s = new GlobalTimeService();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Register(NewEntry(), new StubTimeAware(t1, t2));
        return s;
    }

    [Fact]
    public void CurrentTimeLabel_UtcMode_HasUtcSuffix()
    {
        var svc = WithSamples();
        var tf = new StubTimeFormat(TimeFormat.Utc);
        var vm = new TimelineViewModel(svc, tf);
        Assert.Contains("UTC", vm.CurrentTimeLabel);
    }

    [Fact]
    public void CurrentTimeLabel_LocalMode_HasNoUtcSuffix()
    {
        var svc = WithSamples();
        var tf = new StubTimeFormat(TimeFormat.Local);
        var vm = new TimelineViewModel(svc, tf);
        Assert.DoesNotContain("UTC", vm.CurrentTimeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Switching_Format_Raises_CurrentTimeLabel_Change()
    {
        var svc = WithSamples();
        var tf = new StubTimeFormat(TimeFormat.Local);
        var vm = new TimelineViewModel(svc, tf);
        var changed = new HashSet<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null) changed.Add(e.PropertyName);
        };
        tf.Set(TimeFormat.Utc);
        Assert.Contains(nameof(vm.CurrentTimeLabel), changed);
        Assert.Contains(nameof(vm.RangeLabel), changed);
    }
}
