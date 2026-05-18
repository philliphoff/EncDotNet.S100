using System;
using System.Globalization;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests that station-time-series axis labels honour the active
/// <see cref="TimeFormat"/> setting and refresh live on toggle.
/// </summary>
public class StationTimeSeriesTimeFormatTests
{
    private sealed class StubTimeFormat : ITimeFormatProvider
    {
        public TimeFormat Current { get; private set; }

        public StubTimeFormat(TimeFormat initial) => Current = initial;

        public event Action<TimeFormat>? TimeFormatChanged;

        public void Set(TimeFormat value)
        {
            Current = value;
            TimeFormatChanged?.Invoke(value);
        }
    }

    private static StationTimeSeriesSnapshot Synthetic()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var times = new[] { start, start.AddMinutes(15), start.AddMinutes(30) };
        var values = new[] { 1f, 2f, 3f };
        return new StationTimeSeriesSnapshot
        {
            StationId = "ST01",
            Latitude = 0,
            Longitude = 0,
            Times = times,
            Channels = new[]
            {
                new StationTimeSeriesChannel
                {
                    Key = "waterLevelHeight",
                    DisplayName = "Water Level Height",
                    Unit = "m",
                    Values = values,
                    FillValue = -9999f,
                },
            },
        };
    }

    [Fact]
    public void AxisLabel_UtcMode_ProducesInvariantHourMinute()
    {
        var snap = Synthetic();
        var tf = new StubTimeFormat(TimeFormat.Utc);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null, timeFormat: tf);

        var ticks = (double)new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc).Ticks;
        Assert.Equal("09:30", vm.TimeAxis.Labeler(ticks));
    }

    [Fact]
    public void AxisLabel_LocalMode_UsesCurrentCultureShortTime()
    {
        var snap = Synthetic();
        var tf = new StubTimeFormat(TimeFormat.Local);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null, timeFormat: tf);

        var dt = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var ticks = (double)dt.Ticks;
        var expected = dt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        Assert.Equal(expected, vm.TimeAxis.Labeler(ticks));
    }

    [Fact]
    public void Switching_TimeFormat_Updates_AxisLabel_Live()
    {
        var snap = Synthetic();
        var tf = new StubTimeFormat(TimeFormat.Local);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null, timeFormat: tf);

        var ticks = (double)new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc).Ticks;

        tf.Set(TimeFormat.Utc);

        Assert.Equal("09:30", vm.TimeAxis.Labeler(ticks));
    }

    [Fact]
    public void Tooltip_UtcMode_FormatsAsUtcWithSuffix()
    {
        var snap = Synthetic();
        var tf = new StubTimeFormat(TimeFormat.Utc);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null, timeFormat: tf);

        var utc = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var label = vm.FormatTooltipDateTime(utc);

        Assert.Equal("2026-01-01 09:30 UTC", label);
    }

    [Fact]
    public void Tooltip_LocalMode_FormatsAsLocalShortDateTime()
    {
        var snap = Synthetic();
        var tf = new StubTimeFormat(TimeFormat.Local);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null, timeFormat: tf);

        var utc = new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        var label = vm.FormatTooltipDateTime(utc);

        var expected = utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Equal(expected, label);
        Assert.DoesNotContain("UTC", label);
    }
}
