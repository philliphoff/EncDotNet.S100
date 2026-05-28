using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Core.Tests.DynamicSources;

public class DynamicFeatureTrackerTests
{
    private static DynamicFeature Point(string id, DateTimeOffset t, double lat = 0, double lon = 0) => new()
    {
        Id = id,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { (lat, lon) },
        LastUpdated = t,
    };

    [Fact]
    public void Apply_NewFeature_ReportsAdded()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var t = DateTimeOffset.UtcNow;

        var change = tracker.Apply(Point("a", t));

        Assert.Equal(DynamicSourceChangeKind.Added, change.Kind);
        Assert.Equal(new[] { "a" }, change.ChangedIds);
        Assert.Single(tracker.Current);
    }

    [Fact]
    public void Apply_ExistingFeature_ReportsUpdated()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var t = DateTimeOffset.UtcNow;
        tracker.Apply(Point("a", t));

        var change = tracker.Apply(Point("a", t.AddSeconds(1), lat: 1));

        Assert.Equal(DynamicSourceChangeKind.Updated, change.Kind);
        Assert.Single(tracker.Current);
        Assert.Equal(1.0, tracker.Current[0].Coordinates[0].Latitude);
    }

    [Fact]
    public void Sweep_OldEntries_ReportsRemoved()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var now = DateTimeOffset.UtcNow;
        tracker.Apply(Point("old", now.AddMinutes(-5)));
        tracker.Apply(Point("fresh", now));

        var change = tracker.Sweep(now, TimeSpan.FromMinutes(1));

        Assert.Equal(DynamicSourceChangeKind.Removed, change.Kind);
        Assert.Equal(new[] { "old" }, change.ChangedIds);
        Assert.Single(tracker.Current);
        Assert.Equal("fresh", tracker.Current[0].Id);
    }

    [Fact]
    public void Sweep_NoEntries_ReportsResetWithEmptyIds()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var now = DateTimeOffset.UtcNow;
        tracker.Apply(Point("fresh", now));

        var change = tracker.Sweep(now, TimeSpan.FromMinutes(1));

        Assert.Equal(DynamicSourceChangeKind.Reset, change.Kind);
        Assert.Empty(change.ChangedIds);
        Assert.Single(tracker.Current);
    }

    [Fact]
    public void Remove_ExistingId_ReportsRemoved()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var t = DateTimeOffset.UtcNow;
        tracker.Apply(Point("a", t));

        var change = tracker.Remove("a");

        Assert.Equal(DynamicSourceChangeKind.Removed, change.Kind);
        Assert.Equal(new[] { "a" }, change.ChangedIds);
        Assert.Empty(tracker.Current);
    }

    [Fact]
    public void Remove_UnknownId_ReportsResetNoIds()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);

        var change = tracker.Remove("ghost");

        Assert.Equal(DynamicSourceChangeKind.Reset, change.Kind);
        Assert.Empty(change.ChangedIds);
    }

    [Fact]
    public void Clear_DropsAll_ReportsReset()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var t = DateTimeOffset.UtcNow;
        tracker.Apply(Point("a", t));
        tracker.Apply(Point("b", t));

        var change = tracker.Clear();

        Assert.Equal(DynamicSourceChangeKind.Reset, change.Kind);
        Assert.Empty(tracker.Current);
    }

    [Fact]
    public void Apply_ConcurrentWriters_AllPersist()
    {
        var tracker = new DynamicFeatureTracker<DynamicFeature>(f => f);
        var t = DateTimeOffset.UtcNow;

        Parallel.For(0, 200, i => tracker.Apply(Point($"id-{i}", t)));

        Assert.Equal(200, tracker.Current.Count);
    }
}
