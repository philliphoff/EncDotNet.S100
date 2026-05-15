using EncDotNet.S100.Datasets.S129.Fusion.Timeline;
using EncDotNet.S100.Datasets.S129.Fusion.Tests.Helpers;

namespace EncDotNet.S100.Datasets.S129.Fusion.Tests;

public class TimelineTests
{
    [Fact]
    public void Times_AreSortedAndDeduplicated()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP2", 50.005, 5.002, t0.AddMinutes(10)),
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
            SyntheticDatasets.MakeControlPoint("CP3", 50.005, 5.003, t0.AddMinutes(20)),
            SyntheticDatasets.MakeControlPoint("CP4", 50.005, 5.004, t0.AddMinutes(20)), // duplicate time
            SyntheticDatasets.MakeControlPoint("CPx", 50.005, 5.005, null),               // no time
        });

        var view = new S129TimelineView(plan);

        Assert.Equal(3, view.Times.Length);
        Assert.Equal(t0, view.Times[0]);
        Assert.Equal(t0.AddMinutes(10), view.Times[1]);
        Assert.Equal(t0.AddMinutes(20), view.Times[2]);
        Assert.Equal(t0, view.Start);
        Assert.Equal(t0.AddMinutes(20), view.End);
    }

    [Fact]
    public void EnumerateTimeline_ReturnsOneSnapshotPerDistinctTime()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
            SyntheticDatasets.MakeControlPoint("CP2", 50.005, 5.002, t0.AddMinutes(10)),
            SyntheticDatasets.MakeControlPoint("CP3", 50.005, 5.003, t0.AddMinutes(10)),
        });

        var view = new S129TimelineView(plan);
        var snapshots = view.EnumerateTimeline().ToList();

        Assert.Equal(2, snapshots.Count);
        Assert.True(snapshots[0].IsExact);
        Assert.False(snapshots[0].HasOverlappingControlPoints);
        Assert.True(snapshots[1].HasOverlappingControlPoints);
    }

    [Fact]
    public void GetSnapshotAt_OnGridTime_ReturnsExact()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
            SyntheticDatasets.MakeControlPoint("CP2", 50.005, 5.002, t0.AddMinutes(30)),
        });
        var view = new S129TimelineView(plan);

        var snap = view.GetSnapshotAt(t0.AddMinutes(30));

        Assert.NotNull(snap);
        Assert.True(snap!.IsExact);
        Assert.Equal("CP2", snap.ControlPoint.Id);
    }

    [Fact]
    public void GetSnapshotAt_BetweenGrid_NearestEarlier_PicksPrior()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
            SyntheticDatasets.MakeControlPoint("CP2", 50.005, 5.002, t0.AddMinutes(30)),
        });
        var view = new S129TimelineView(plan);

        var snap = view.GetSnapshotAt(t0.AddMinutes(10));

        Assert.NotNull(snap);
        Assert.False(snap!.IsExact);
        Assert.Equal("CP1", snap.ControlPoint.Id);
    }

    [Fact]
    public void GetSnapshotAt_BeforeFirst_NearestEarlier_ReturnsNull()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
        });
        var view = new S129TimelineView(plan);

        Assert.Null(view.GetSnapshotAt(t0.AddMinutes(-5)));
    }

    [Fact]
    public void GetSnapshotAt_Modes_BehaveAsDocumented()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
            SyntheticDatasets.MakeControlPoint("CP2", 50.005, 5.002, t0.AddMinutes(30)),
        });
        var view = new S129TimelineView(plan);

        // Between samples — Nearest picks the closer
        var nearest = view.GetSnapshotAt(t0.AddMinutes(20), S129TimelineSamplingMode.Nearest);
        Assert.Equal("CP2", nearest!.ControlPoint.Id);

        var later = view.GetSnapshotAt(t0.AddMinutes(5), S129TimelineSamplingMode.NearestLater);
        Assert.Equal("CP2", later!.ControlPoint.Id);

        Assert.Null(view.GetSnapshotAt(t0.AddMinutes(40), S129TimelineSamplingMode.NearestLater));
        Assert.Null(view.GetSnapshotAt(t0.AddMinutes(20), S129TimelineSamplingMode.Exact));
        Assert.NotNull(view.GetSnapshotAt(t0, S129TimelineSamplingMode.Exact));
    }

    [Fact]
    public void EmptyPlan_TimelineIsEmpty()
    {
        var plan = SyntheticDatasets.MakePlan(); // metadata only
        var view = new S129TimelineView(plan);

        Assert.True(view.IsEmpty);
        Assert.Null(view.Start);
        Assert.Null(view.End);
        Assert.Null(view.GetSnapshotAt(SyntheticDatasets.T0));
    }
}
