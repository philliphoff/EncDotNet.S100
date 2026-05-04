using EncDotNet.S100.Viewer.Tools;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class MeasurePathStateTests
{
    [Fact]
    public void NewState_IsIdleWithNoWaypoints()
    {
        var s = new MeasurePathState();
        Assert.Equal(MeasurePathState.MeasurePhase.Idle, s.Phase);
        Assert.Empty(s.Waypoints);
        Assert.Null(s.RubberBand);
    }

    [Fact]
    public void Click_FromIdle_PlacesFirstWaypointAndEntersDrawing()
    {
        var s = new MeasurePathState();
        var changed = s.Click(40.0, -74.0);
        Assert.True(changed);
        Assert.Equal(MeasurePathState.MeasurePhase.Drawing, s.Phase);
        Assert.Single(s.Waypoints);
    }

    [Fact]
    public void Click_FromDrawing_AppendsWaypoint()
    {
        var s = new MeasurePathState();
        s.Click(40.0, -74.0);
        s.Click(38.7, -9.1);
        Assert.Equal(2, s.Waypoints.Count);
        Assert.Equal(MeasurePathState.MeasurePhase.Drawing, s.Phase);
    }

    [Fact]
    public void Finalise_FromDrawingWithWaypoints_TransitionsToFinalised()
    {
        var s = new MeasurePathState();
        s.Click(40.0, -74.0);
        s.Click(38.7, -9.1);
        Assert.True(s.Finalise());
        Assert.Equal(MeasurePathState.MeasurePhase.Finalised, s.Phase);
        Assert.Null(s.RubberBand);
    }

    [Fact]
    public void Finalise_FromIdleOrFinalised_IsNoOp()
    {
        var s = new MeasurePathState();
        Assert.False(s.Finalise()); // idle, no waypoints

        s.Click(0.0, 0.0);
        s.Finalise();
        Assert.False(s.Finalise()); // already finalised
    }

    [Fact]
    public void Click_AfterFinalised_ClearsAndStartsNewPath()
    {
        var s = new MeasurePathState();
        s.Click(0.0, 0.0);
        s.Click(1.0, 1.0);
        s.Finalise();

        s.Click(40.0, -74.0);
        Assert.Equal(MeasurePathState.MeasurePhase.Drawing, s.Phase);
        Assert.Single(s.Waypoints);
    }

    [Fact]
    public void Backstep_RemovesLastWaypoint_AndReturnsToIdleWhenEmpty()
    {
        var s = new MeasurePathState();
        s.Click(0.0, 0.0);
        s.Click(1.0, 1.0);

        Assert.True(s.Backstep());
        Assert.Single(s.Waypoints);
        Assert.Equal(MeasurePathState.MeasurePhase.Drawing, s.Phase);

        Assert.True(s.Backstep());
        Assert.Empty(s.Waypoints);
        Assert.Equal(MeasurePathState.MeasurePhase.Idle, s.Phase);
    }

    [Fact]
    public void Backstep_FromFinalised_ClearsEntirePath()
    {
        var s = new MeasurePathState();
        s.Click(0.0, 0.0);
        s.Click(1.0, 1.0);
        s.Finalise();

        Assert.True(s.Backstep());
        Assert.Empty(s.Waypoints);
        Assert.Equal(MeasurePathState.MeasurePhase.Idle, s.Phase);
    }

    [Fact]
    public void Hover_OnlyUpdatesInDrawingPhase()
    {
        var s = new MeasurePathState();
        // Idle → no rubber band even with cursor.
        s.Hover((10.0, 10.0));
        Assert.Null(s.RubberBand);

        s.Click(0.0, 0.0);
        s.Hover((1.0, 1.0));
        Assert.NotNull(s.RubberBand);

        s.Finalise();
        s.Hover((2.0, 2.0));
        Assert.Null(s.RubberBand);
    }

    [Fact]
    public void TotalDistance_SumsLegLengths()
    {
        var s = new MeasurePathState();
        s.Click(0.0, 0.0);
        s.Click(0.0, 1.0); // 1° east at equator ≈ 60 NM
        s.Click(0.0, 2.0); // another 60 NM
        s.Finalise();

        var total = s.TotalDistanceNm();
        // Two ~60 NM legs; allow some tolerance for Earth radius constants.
        Assert.InRange(total, 119.0, 121.0);
    }
}
