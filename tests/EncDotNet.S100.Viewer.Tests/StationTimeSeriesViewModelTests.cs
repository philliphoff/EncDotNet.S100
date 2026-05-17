using System;
using System.Linq;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using LiveChartsCore.Defaults;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for the PR-J station time-series view models. Validates the
/// fill-value filter, channel projection, now-marker subscription, and the
/// PickHit ↔ FeatureInfo plumbing for station picks.
/// </summary>
public class StationTimeSeriesViewModelTests
{
    private static StationTimeSeriesSnapshot SyntheticWaterLevel(
        int n = 4,
        float? withFillAt = null)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromMinutes(15);
        var times = Enumerable.Range(0, n).Select(i => start + step * i).ToArray();
        var values = Enumerable.Range(0, n).Select(i => (float)(1.0 + 0.1 * i)).ToArray();
        if (withFillAt is { } idx && (int)idx is var ix && ix >= 0 && ix < n)
            values[ix] = -9999f;
        return new StationTimeSeriesSnapshot
        {
            StationId = "ST01",
            Latitude = 50.0,
            Longitude = -4.0,
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

    private static StationTimeSeriesSnapshot SyntheticSurfaceCurrent(int n = 5)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var step = TimeSpan.FromMinutes(30);
        var times = Enumerable.Range(0, n).Select(i => start + step * i).ToArray();
        var speeds = Enumerable.Range(0, n).Select(i => 0.5f + 0.1f * i).ToArray();
        var directions = Enumerable.Range(0, n).Select(i => 45f + 10f * i).ToArray();
        return new StationTimeSeriesSnapshot
        {
            StationId = "ST02",
            Latitude = 50.0,
            Longitude = -4.0,
            Times = times,
            Channels = new[]
            {
                new StationTimeSeriesChannel
                {
                    Key = "surfaceCurrentSpeed",
                    DisplayName = "Surface Current Speed",
                    Unit = "m/s",
                    Values = speeds,
                    FillValue = -9999f,
                },
                new StationTimeSeriesChannel
                {
                    Key = "surfaceCurrentDirection",
                    DisplayName = "Surface Current Direction",
                    Unit = "°",
                    Values = directions,
                    FillValue = -9999f,
                },
            },
        };
    }

    [Fact]
    public void S104_ProjectionMatchesSnapshotPoints()
    {
        var snap = SyntheticWaterLevel(n: 4);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null);

        Assert.Single(vm.HeightSeries);
        var series = (LiveChartsCore.SkiaSharpView.LineSeries<DateTimePoint>)vm.HeightSeries[0];
        var points = series.Values!.Cast<DateTimePoint>().ToList();

        Assert.Equal(4, points.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(snap.Times[i], points[i].DateTime);
            Assert.Equal(snap.Channels[0].Values[i], (float)points[i].Value!.Value);
        }
    }

    [Fact]
    public void S104_FillValueSamplesAreDropped()
    {
        var snap = SyntheticWaterLevel(n: 4, withFillAt: 2);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null);
        var series = (LiveChartsCore.SkiaSharpView.LineSeries<DateTimePoint>)vm.HeightSeries[0];
        var points = series.Values!.Cast<DateTimePoint>().ToList();
        Assert.Equal(3, points.Count);
        Assert.DoesNotContain(points, p => p.Value == -9999.0);
    }

    [Fact]
    public void S111_ProducesSpeedAndDirectionSeries()
    {
        var snap = SyntheticSurfaceCurrent(n: 5);
        using var vm = new S111StationTimeSeriesViewModel(snap, globalTime: null);

        var speed = (LiveChartsCore.SkiaSharpView.LineSeries<DateTimePoint>)vm.SpeedSeries[0];
        var direction = (LiveChartsCore.SkiaSharpView.LineSeries<DateTimePoint>)vm.DirectionSeries[0];
        Assert.Equal(5, speed.Values!.Cast<DateTimePoint>().Count());
        Assert.Equal(5, direction.Values!.Cast<DateTimePoint>().Count());

        // The two charts must share the same x-axis instance so they stack
        // with aligned time tick marks.
        Assert.Same(vm.TimeAxis, vm.TimeAxisArray[0]);
    }

    [Fact]
    public void NowMarker_TracksGlobalTimeService()
    {
        var snap = SyntheticWaterLevel(n: 3);
        var globalTime = new GlobalTimeService();
        var adapter = new FakeTimeAware(snap.Times);
        globalTime.Register(new DatasetEntry("/tmp/x.h5", "S-104"), adapter);

        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime);
        var initialXi = vm.Sections[0].Xi;

        globalTime.SetCurrentTime(snap.Times[2]);
        Assert.Equal((double)snap.Times[2].Ticks, vm.Sections[0].Xi);
        Assert.NotEqual(initialXi, vm.Sections[0].Xi);
    }

    [Fact]
    public void UpdateNowMarker_SetsBothEndsToSameTime()
    {
        var snap = SyntheticWaterLevel(n: 3);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null);
        var t = new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        vm.UpdateNowMarker(t);
        Assert.Equal((double)t.Ticks, vm.Sections[0].Xi);
        Assert.Equal((double)t.Ticks, vm.Sections[0].Xj);
    }

    [Fact]
    public void PickHit_StationSeriesPopulatedFromFeatureInfo()
    {
        var snap = SyntheticWaterLevel(n: 3);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null);

        var hit = new PickHit
        {
            FeatureType = "WaterLevel",
            FeatureRef = "station:ST01",
            StationSeries = vm,
        };
        Assert.NotNull(hit.StationSeries);
        Assert.Same(vm, hit.StationSeries);
    }

    [Fact]
    public void PickHit_StationSeriesIsNullWhenInfoCarriesNoSnapshot()
    {
        var hit = new PickHit
        {
            FeatureType = "DepthArea",
            FeatureRef = "feat:1",
        };
        Assert.Null(hit.StationSeries);
    }

    [Fact]
    public void PickReportViewModel_SurfacesSelectedStationSeries()
    {
        var snap = SyntheticWaterLevel(n: 3);
        using var vm = new S104StationTimeSeriesViewModel(snap, globalTime: null);

        var report = new PickReportViewModel();
        report.SetPicks(new[]
        {
            new PickHit
            {
                FeatureType = "WaterLevel",
                FeatureRef = "station:ST01",
                StationSeries = vm,
            },
        });
        Assert.True(report.HasStationSeries);
        Assert.Same(vm, report.SelectedStationSeries);

        report.SetPicks(new[]
        {
            new PickHit { FeatureType = "DepthArea", FeatureRef = "f1" },
        });
        Assert.False(report.HasStationSeries);
        Assert.Null(report.SelectedStationSeries);
    }

    private sealed class FakeTimeAware : ITimeAwareDataset
    {
        public FakeTimeAware(System.Collections.Generic.IReadOnlyList<DateTime> times)
        {
            AvailableTimes = times;
        }

        public System.Collections.Generic.IReadOnlyList<DateTime> AvailableTimes { get; }
        public DateTime? CurrentTime { get; private set; }
        public DateTime? SnapTo(DateTime t) => t;
    }
}
