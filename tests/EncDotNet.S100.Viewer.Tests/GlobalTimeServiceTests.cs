using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

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
    public void AttachTo_publishes_existing_session_time_state()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = new MapsuiDatasetLayerSession(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = new MapDatasetId("timed");
        Assert.True(owner.TryRegister(id, new SessionTimeProcessor(time)));
        session.SetDataset(new MapDataset(
            id,
            id.Value,
            new DatasetMetadata
            {
                Spec = new SpecRef("S-104", new SpecVersion(1, 0, 0)),
            }));
        var service = new GlobalTimeService();
        var rangeChanged = 0;
        DateTime? currentChanged = null;
        service.RangeChanged += () => rangeChanged++;
        service.CurrentTimeChanged += current => currentChanged = current;

        service.AttachTo(session);

        Assert.Equal(1, rangeChanged);
        Assert.Equal(time, currentChanged);
        Assert.Equal(time, service.CurrentTime);
        var timed = Assert.Single(service.TimedDatasets);
        Assert.Equal(("timed", time, time), (timed.Name, timed.First, timed.Last));
        // One S-104 sample: at or before, held for an hour.
        Assert.Equal(new MapsuiMapTimeSegment(time, time.AddHours(1)), Assert.Single(timed.Coverage));
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
    public void SetCurrentTime_is_not_clamped_and_pins_the_time()
    {
        var s = new GlobalTimeService();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        s.Register(NewEntry(), new StubTimeAware(t1, t2));
        // After Register, CurrentTime auto-initialises to MinTime (t1).
        Assert.Equal(t1, s.CurrentTime);

        DateTime? observed = null;
        s.CurrentTimeChanged += t => observed = t;

        // The view time can sit past the data (#713 A3).
        s.SetCurrentTime(t2.AddHours(99));
        Assert.Equal(t2.AddHours(99), observed);
        Assert.Equal(t2.AddHours(99), s.CurrentTime);
        Assert.Equal(TimeMode.Pinned, s.Mode);

        s.GoLive(t1.AddHours(-1));
        Assert.Equal(t1.AddHours(-1), observed);
        Assert.Equal(TimeMode.Live, s.Mode);
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

    [Fact]
    public void TimelineViewModel_uses_real_samples_as_ticks_when_few()
    {
        var s = new GlobalTimeService();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Register(NewEntry(), new StubTimeAware(t1, t2, t3));

        // Now inside the data, so the axis (which always includes now) is just the data.
        var vm = new TimelineViewModel(s, null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(t2)));

        Assert.Equal(3, vm.Ticks.Count);
        Assert.True(vm.IsSnapToTickEnabled);
        // Ticks are normalized [0,1] positions on the gap-collapsing axis;
        // a contiguous range maps linearly, so the samples land at 0, 0.5, 1.
        Assert.Equal(0.0, vm.Ticks[0], 6);
        Assert.Equal(0.5, vm.Ticks[1], 6);
        Assert.Equal(1.0, vm.Ticks[2], 6);
    }

    [Fact]
    public void TimelineViewModel_shows_no_ticks_when_samples_are_too_dense()
    {
        var s = new GlobalTimeService();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new DateTime[2000];
        for (var i = 0; i < samples.Length; i++) samples[i] = t0.AddMinutes(i);
        s.Register(NewEntry(), new StubTimeAware(samples));

        var vm = new TimelineViewModel(s, null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(t0.AddHours(1))));

        // Samples under 0.7 % of the axis apart: no ticks, no snapping; the
        // band reads as solid data (#708 C4). Stepping still works.
        Assert.Empty(vm.Ticks);
        Assert.False(vm.IsSnapToTickEnabled);
        Assert.True(vm.AreStepButtonsVisible);
    }

    [Fact]
    public void TimelineViewModel_step_commands_advance_by_the_chosen_step()
    {
        var s = new GlobalTimeService();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        s.Register(NewEntry(), new StubTimeAware(t1, t2, t3));

        // Now inside the data, so the axis (which always includes now) is just the data.
        var vm = new TimelineViewModel(s, null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(t2)));

        // The default step is 1 h (#708 C6).
        Assert.Equal(t1, s.CurrentTime);
        Assert.False(vm.PreviousStepCommand.CanExecute(null));
        vm.NextStepCommand.Execute(null);
        Assert.Equal(t1.AddHours(1), s.CurrentTime);

        // "Sample of" the layer walks its samples.
        vm.SetStepCommand.Execute(nameof(TimelineStepKind.Sample));
        vm.NextStepCommand.Execute(null);
        Assert.Equal(t2, s.CurrentTime);
        vm.NextStepCommand.Execute(null);
        Assert.Equal(t3, s.CurrentTime);
        Assert.False(vm.NextStepCommand.CanExecute(null));
        vm.PreviousStepCommand.Execute(null);
        Assert.Equal(t2, s.CurrentTime);
    }

    private sealed class IdentityCrsTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) =>
            IdentityCrsTransform.Instance;
    }

    [Fact]
    public void Loading_a_forecast_that_covers_now_stays_live_at_now_in_a_real_session()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = new MapsuiDatasetLayerSession(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));
        var run = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var now = run.AddHours(4).AddMinutes(34);
        var service = new GlobalTimeService();
        service.AttachTo(session);
        // As DatasetLoaderService does: every clock change is re-applied to the session.
        service.CurrentTimeChanged += time => session.SetCurrentTime(time);
        _ = new TimelineViewModel(service, null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(now)), action => action());
        var id = new MapDatasetId("111US00_CBOFS_US4MD1DD");
        Assert.True(owner.TryRegister(id, new SessionTimeProcessor([.. Enumerable.Range(0, 49).Select(h => run.AddHours(h))], "S-111")));

        session.SetDataset(new MapDataset(
            id,
            id.Value,
            new DatasetMetadata { Spec = new SpecRef("S-111", new SpecVersion(1, 0, 0)) }));

        // The range change's trailing clock event must not put the first step back (#713).
        Assert.Equal(TimeMode.Live, service.Mode);
        Assert.Equal(now, service.CurrentTime);
        Assert.Equal(now, session.GetTimeSnapshot().Current);
    }

    private sealed class SessionTimeProcessor(IReadOnlyList<DateTime> times, string spec = "S-104") :
        IDatasetProcessor,
        ITimeAwareDatasetProcessor
    {
        public SessionTimeProcessor(DateTime time)
            : this([time])
        {
        }

        public SpecRef Spec => new(spec, new SpecVersion(1, 0, 0));

        public IReadOnlyList<DateTime> AvailableTimes { get; } = times;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }
}
