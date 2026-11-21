using System.Diagnostics;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Engine.Prediction;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Map rendering slice&#160;9: the view history, the prediction model, the
/// demand planner, the arrival forecast and the prediction stats.
/// </summary>
public class DemandPlannerTests
{
    private static readonly long Second = Stopwatch.Frequency;

    private static ViewState View(double x = 0, double y = 0, double resolution = 10) =>
        new(x, y, resolution, 0, 1000, 800);

    private static long At(double seconds) => (long)(seconds * Second) + (1000 * Second);

    [Fact]
    public void History_EstimatesVelocityAndZoomRateWhileMoving()
    {
        var history = new ViewHistory();
        for (var i = 0; i <= 20; i++)
        {
            // 1000 m/s east, zooming out by e^0.5 per second.
            history.Add(View(x: i * 50, resolution: 10 * Math.Exp(0.5 * i * 0.05)), At(i * 0.05));
        }

        Assert.True(history.IsMoving(At(1.0)));
        Assert.Equal(1000, history.VelocityX, 0);
        Assert.Equal(0, history.VelocityY, 6);
        Assert.Equal(0.5, history.ZoomRate, 2);
        Assert.False(history.IsMoving(At(2.0)));
    }

    [Fact]
    public void History_AJumpAfterAPauseHasNoVelocity()
    {
        var history = new ViewHistory();
        history.Add(View(), At(0));
        history.Add(View(x: 50_000), At(5));

        Assert.Equal(0, history.VelocityX);
        Assert.Equal(0, history.ZoomRate);
    }

    [Fact]
    public void History_RemembersPlacesItRestedAt()
    {
        var history = new ViewHistory();
        history.Add(View(), At(0));
        history.Add(View(x: 100_000), At(2));
        history.Add(View(x: 200_000), At(2.1));
        history.NoteIdle(At(5));

        Assert.Equal([View(x: 200_000), View()], history.RecentPlaces);
        Assert.Equal(View(), history.Start);
    }

    [Fact]
    public void Model_ADestinationIsTheStrongestPrediction()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        var target = View(x: 30_000, resolution: 5);
        planner.BeginAnimation(target, TimeSpan.FromMilliseconds(800), At(0));

        var plan = planner.Plan(At(0.2));

        var first = plan.Views[0];
        Assert.Equal(PredictionSource.Destination, first.Source);
        Assert.Equal(target, first.View);
        Assert.Equal(PredictionModel.AnimationProbability, first.Probability);
        Assert.Equal(0.6, first.Horizon.TotalSeconds, 3);
        Assert.Contains(plan.TileViews, v => v.Source == PredictionSource.Destination);
        Assert.Contains(plan.CellViews, v => v.Source == PredictionSource.Destination);
    }

    [Fact]
    public void Model_ExtrapolatesThePanAndZoomWhileMoving()
    {
        var planner = new DemandPlanner();
        for (var i = 0; i <= 10; i++)
        {
            planner.Observe(View(y: i * 100, resolution: 10 * Math.Exp(-0.5 * i * 0.05)), At(i * 0.05));
        }

        var plan = planner.Plan(At(0.55));
        var motion = plan.Views.Where(v => v.Source == PredictionSource.Motion).ToList();

        Assert.Equal(4, motion.Count);
        var oneSecond = motion.Single(v => v.Horizon == TimeSpan.FromSeconds(1));
        Assert.True(oneSecond.View.CenterY > 1000 + 1500, $"centre {oneSecond.View.CenterY}");
        Assert.True(oneSecond.View.Resolution < plan.Current!.Value.Resolution);
        Assert.DoesNotContain(plan.TileViews, v => v.Horizon > DemandPlanner.TileHorizon);
        Assert.DoesNotContain(plan.CellViews, v => v.Horizon > DemandPlanner.CellHorizon);
    }

    [Fact]
    public void Model_PredictsNothingFromMotionOnceTheViewStops()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        planner.Observe(View(x: 100), At(0.05));
        planner.Observe(View(x: 200), At(0.1));

        Assert.DoesNotContain(planner.Plan(At(3)).Views, v => v.Source == PredictionSource.Motion);
    }

    [Fact]
    public void Model_UsesTheRouteAheadAndRecentPlaces()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        planner.Observe(View(x: 500_000), At(3));
        planner.SetInputs(new PredictionInputs
        {
            RouteAhead = [new WorldPoint(510_000, 0), new WorldPoint(520_000, 0)],
            OwnShipSpeed = 5,
        });

        var plan = planner.Plan(At(10));

        var route = plan.Views.Where(v => v.Source == PredictionSource.Route).ToList();
        Assert.Equal(2, route.Count);
        Assert.True(route[0].Probability > route[1].Probability);
        Assert.Equal(2000, route[0].Horizon.TotalSeconds, 0);
        var history = Assert.Single(plan.Views, v => v.Source == PredictionSource.History);
        Assert.Equal(View(), history.View);
        Assert.Contains(plan.CellViews, v => v.Source == PredictionSource.History);
    }

    [Fact]
    public void Planner_ADestinationEndsWhenTheViewArrives()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        var target = View(x: 30_000);
        planner.BeginAnimation(target, TimeSpan.FromMilliseconds(500), At(0));
        Assert.True(planner.Motion.HasTarget);

        planner.Observe(target, At(0.5));

        Assert.Equal(ViewMotionKind.Idle, planner.Motion.Kind);
    }

    [Fact]
    public void Planner_ADestinationEndsAfterItsArrivalTimeEvenIfNeverShown()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        planner.BeginAnimation(View(x: 30_000), TimeSpan.FromMilliseconds(500), At(0));

        var plan = planner.Plan(At(0.5) + (Second / 2));

        Assert.Equal(ViewMotionKind.Idle, plan.Motion.Kind);
        Assert.DoesNotContain(plan.Views, v => v.Source == PredictionSource.Destination);
    }

    [Fact]
    public void Planner_RaisesDestinationChanged()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        var raised = 0;
        planner.DestinationChanged += (_, _) => raised++;

        planner.BeginAnimation(View(x: 1), TimeSpan.FromMilliseconds(100), At(0));
        planner.BeginFling(10, 0, TimeSpan.FromMilliseconds(100), At(0));

        Assert.Equal(2, raised);
        Assert.Equal(2, planner.Stats.Snapshot().Destinations);
    }

    [Fact]
    public void Planner_LearnsHowFarFlingsGo()
    {
        var planner = new DemandPlanner();
        planner.Observe(View(), At(0));
        var target = planner.BeginFling(1000, 0, TimeSpan.FromMilliseconds(400), At(0));
        Assert.Equal(1000, target!.Value.CenterX);

        // The fling went twice as far as predicted, then rested.
        planner.Observe(View(x: 1000), At(0.2));
        planner.Observe(View(x: 2000), At(0.4));
        planner.Plan(At(1.0));

        Assert.Equal(ViewMotionKind.Idle, planner.Motion.Kind);
        Assert.Equal(1.5, planner.FlingScale, 6);
        var stats = planner.Stats.Snapshot();
        Assert.Equal(1, stats.Flings);
        Assert.Equal(1.0, stats.FlingError!.Value, 6);

        planner.Observe(View(x: 2000), At(2));
        var next = planner.BeginFling(1000, 0, TimeSpan.FromMilliseconds(400), At(2));
        Assert.Equal(3500, next!.Value.CenterX);
    }

    [Fact]
    public void Planner_BeginFlingNeedsAView()
    {
        Assert.Null(new DemandPlanner().BeginFling(10, 0, TimeSpan.FromMilliseconds(100), At(0)));
    }

    [Fact]
    public void Arrivals_CountQueuedAndInFlightLoads()
    {
        var arrivals = new ArrivalForecast();
        var a = new object();
        var b = new object();
        var version = arrivals.Version;

        arrivals.SetQueued(2);
        arrivals.Expect(a);
        arrivals.Expect(b);
        arrivals.Expect(b);
        Assert.Equal(4, arrivals.Pending);
        Assert.True(arrivals.Version > version);

        arrivals.Resolve(a);
        arrivals.Resolve(new object());
        arrivals.Resolve(null);
        Assert.Equal(3, arrivals.Pending);

        arrivals.SetQueued(0);
        arrivals.Resolve(b);
        Assert.Equal(0, arrivals.Pending);

        arrivals.Expect(a);
        arrivals.Clear();
        Assert.Equal(0, arrivals.Pending);
    }

    [Fact]
    public void Stats_ReportRatesOverAWindow()
    {
        var stats = new PredictionStats();
        stats.RecordSlots(1, 1);
        var start = stats.Snapshot();

        stats.RecordSlots(3, 1);
        stats.RecordTilesPredicted(5);
        stats.RecordTilesUsed(3);
        stats.RecordTilesWasted(1);
        stats.RecordCellEntered(loaded: true);
        stats.RecordCellEntered(loaded: false);
        stats.RecordCellPredicted();
        stats.RecordCellUsed();
        stats.RecordCellWasted();
        stats.RecordCellWasted();
        stats.RecordHold(TimeSpan.FromMilliseconds(20));

        var window = stats.Snapshot().Since(start);

        Assert.Equal(0.75, window.SlotHitRate);
        Assert.Equal(0.25, window.TileWasteRate);
        Assert.Equal(0.5, window.CellHitRate);
        Assert.Equal(2.0 / 3.0, window.CellWasteRate!.Value, 6);
        Assert.Equal(1, window.Holds);
        Assert.Equal(20, window.HoldTime.TotalMilliseconds, 3);
        Assert.Null(new PredictionStats().Snapshot().SlotHitRate);
    }

    [Fact]
    public void ChartSession_HasAPlanner()
    {
        var planner = new DemandPlanner();
        Assert.Same(planner, new ChartSession(new ChartSessionOptions(), planner).Planner);
        Assert.NotNull(new ChartSession(new ChartSessionOptions()).Planner);
    }
}
