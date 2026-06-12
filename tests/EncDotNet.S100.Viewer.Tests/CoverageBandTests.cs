using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for the timeline data-coverage band: the merged
/// <see cref="GlobalTimeService.CoverageSegments"/> and their
/// normalization to <see cref="TimelineViewModel.CoverageBands"/>.
/// </summary>
public sealed class CoverageBandTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Stub allowing explicit coverage intervals (e.g. open-ended S-411).</summary>
    private sealed class Stub : ITimeAwareDataset
    {
        private readonly IReadOnlyList<(DateTime, DateTime)>? _intervals;
        public Stub(DateTime[] times, IReadOnlyList<(DateTime, DateTime)>? intervals = null)
        {
            AvailableTimes = times;
            _intervals = intervals;
        }
        public IReadOnlyList<DateTime> AvailableTimes { get; }
        public DateTime? CurrentTime => null;
        public DateTime? SnapTo(DateTime t) => AvailableTimes.Count == 0 ? null : AvailableTimes[0];
        public IReadOnlyList<(DateTime Start, DateTime End)> CoverageIntervals
        {
            get
            {
                if (_intervals is not null) return _intervals;
                if (AvailableTimes.Count == 0) return Array.Empty<(DateTime, DateTime)>();
                var min = AvailableTimes.Min();
                var max = AvailableTimes.Max();
                return new[] { (min, max) };
            }
        }
    }

    private static DatasetEntry NewEntry() => new("/tmp/d", "S111");

    [Fact]
    public void Empty_service_has_no_coverage()
    {
        var s = new GlobalTimeService();
        Assert.Empty(s.CoverageSegments);
    }

    [Fact]
    public void Touching_ranges_merge_into_one_segment()
    {
        var s = new GlobalTimeService();
        s.Register(NewEntry(), new Stub(new[] { T0, T0.AddHours(2) }));
        s.Register(NewEntry(), new Stub(new[] { T0.AddHours(2), T0.AddHours(4) }));

        var seg = Assert.Single(s.CoverageSegments);
        Assert.Equal(T0, seg.Start);
        Assert.Equal(T0.AddHours(4), seg.End);
    }

    [Fact]
    public void Disjoint_ranges_stay_separate_with_a_gap()
    {
        var s = new GlobalTimeService();
        s.Register(NewEntry(), new Stub(new[] { T0, T0.AddHours(2) }));
        s.Register(NewEntry(), new Stub(new[] { T0.AddHours(8), T0.AddHours(10) }));

        Assert.Equal(2, s.CoverageSegments.Count);
        Assert.Equal(T0, s.CoverageSegments[0].Start);
        Assert.Equal(T0.AddHours(2), s.CoverageSegments[0].End);
        Assert.Equal(T0.AddHours(8), s.CoverageSegments[1].Start);
        Assert.Equal(T0.AddHours(10), s.CoverageSegments[1].End);
    }

    [Fact]
    public void Open_ended_interval_is_clamped_to_max_time()
    {
        var s = new GlobalTimeService();
        // Dataset A defines the aggregate range [T0, T0+10h].
        s.Register(NewEntry(), new Stub(new[] { T0, T0.AddHours(10) }));
        // Dataset B is an open-ended snapshot starting at T0+5h.
        s.Register(NewEntry(), new Stub(
            new[] { T0.AddHours(5) },
            new[] { (T0.AddHours(5), DateTime.MaxValue) }));

        // Both merge; the open end must not exceed MaxTime.
        var seg = Assert.Single(s.CoverageSegments);
        Assert.Equal(T0, seg.Start);
        Assert.Equal(T0.AddHours(10), seg.End);
    }

    [Fact]
    public void CoverageBands_collapse_gaps_on_the_axis()
    {
        var s = new GlobalTimeService();
        s.Register(NewEntry(), new Stub(new[] { T0, T0.AddHours(2) }));
        s.Register(NewEntry(), new Stub(new[] { T0.AddHours(8), T0.AddHours(10) }));

        var vm = new TimelineViewModel(s);
        var bands = vm.CoverageBands;

        // Two equal 2h data clusters separated by a 6h gap. On a linear axis
        // each cluster would be 0.2 wide; the gap-collapsing axis compresses
        // the gap (to 12% of data width) so each cluster expands to ~0.446
        // and they stay selectable.
        Assert.Equal(2, bands.Count);
        Assert.Equal(0.0, bands[0].Start, 3);
        Assert.Equal(0.446, bands[0].Width, 3);
        Assert.Equal(0.554, bands[1].Start, 3);
        Assert.Equal(0.446, bands[1].Width, 3);
        // The compressed gap between the clusters is thin but non-zero.
        double gap = bands[1].Start - (bands[0].Start + bands[0].Width);
        Assert.True(gap is > 0.0 and < 0.2, $"gap was {gap}");
    }

    [Fact]
    public void CoverageBands_empty_for_degenerate_range()
    {
        var s = new GlobalTimeService();
        s.Register(NewEntry(), new Stub(new[] { T0 })); // single sample => min==max

        var vm = new TimelineViewModel(s);
        Assert.Empty(vm.CoverageBands);
    }

    [Fact]
    public void CoverageBands_property_changes_when_range_changes()
    {
        var s = new GlobalTimeService();
        var vm = new TimelineViewModel(s);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.Register(NewEntry(), new Stub(new[] { T0, T0.AddHours(4) }));

        Assert.Contains(nameof(TimelineViewModel.CoverageBands), raised);
        Assert.Single(vm.CoverageBands);
    }
}
