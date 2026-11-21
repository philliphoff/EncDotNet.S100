using System.Diagnostics;

namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>
/// Turns the view and the prediction into demand (map rendering
/// slice&#160;9): tile demand about a second ahead, which the chart layers
/// rasterise as predicted tiles, and cell demand a few seconds ahead, which
/// the host's cell loader reads through <see cref="Plan(long)"/>. It also carries
/// the <see cref="Arrivals"/> forecast the chart stack batches versions by,
/// and the <see cref="Stats"/> on how well the prediction does.
/// </summary>
/// <remarks>
/// A host reports every view with <see cref="Observe"/> and, when it starts
/// one, a fling or an animation with <see cref="BeginFling"/> or
/// <see cref="BeginAnimation"/>. Thread-safe; every call is cheap.
/// </remarks>
public sealed class DemandPlanner
{
    /// <summary>How far ahead tile demand looks.</summary>
    public static readonly TimeSpan TileHorizon = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How far ahead cell demand looks. With Lua-CSharp the slowest cell
    /// portrays in about 1.3&#160;s, so a few seconds is enough.
    /// </summary>
    public static readonly TimeSpan CellHorizon = TimeSpan.FromSeconds(4);

    /// <summary>
    /// A motion counts as over this long after its arrival time, even if the
    /// host never showed its target (a limit stopped it, or a gesture took over).
    /// </summary>
    public static readonly TimeSpan ArrivalGrace = TimeSpan.FromMilliseconds(300);

    // Bounds of the fling calibration.
    private const double MinFlingScale = 0.25;
    private const double MaxFlingScale = 4.0;
    private const double FlingScaleAlpha = 0.5;

    private readonly Lock _sync = new();
    private readonly IPredictionModel _model;
    private readonly ViewHistory _history = new();
    private PredictionInputs _inputs = PredictionInputs.None;
    private ViewState? _motionOrigin;
    private double _flingScale = 1.0;

    /// <summary>Creates a planner.</summary>
    /// <param name="model">The prediction model; <see cref="PredictionModel"/> when null.</param>
    public DemandPlanner(IPredictionModel? model = null)
    {
        _model = model ?? new PredictionModel();
    }

    /// <summary>The cells the current view is still waiting for.</summary>
    public ArrivalForecast Arrivals { get; } = new();

    /// <summary>How well the prediction does.</summary>
    public PredictionStats Stats { get; } = new();

    /// <summary>
    /// Raised, off the caller's lock, when the host reports a fling or an
    /// animation: a cell loader should plan again now rather than wait for the
    /// view to settle.
    /// </summary>
    public event EventHandler? DestinationChanged;

    /// <summary>The motion the host last reported, until it ends.</summary>
    public ViewMotion Motion
    {
        get
        {
            lock (_sync)
            {
                return _history.Motion;
            }
        }
    }

    /// <summary>
    /// The factor the predicted fling travel is scaled by, learnt from where
    /// earlier flings came to rest (the host's fling decelerates per frame, so
    /// its travel depends on the frame rate).
    /// </summary>
    public double FlingScale
    {
        get
        {
            lock (_sync)
            {
                return _flingScale;
            }
        }
    }

    /// <summary>Reports the view the host shows now.</summary>
    /// <param name="view">The view.</param>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    public void Observe(ViewState view, long timestamp)
    {
        lock (_sync)
        {
            _history.Add(view, timestamp);
            EndMotionIfDone(timestamp);
        }
    }

    /// <summary>
    /// Reports that an animation to <paramref name="target"/> started at
    /// <paramref name="timestamp"/> and takes <paramref name="duration"/>.
    /// </summary>
    /// <param name="target">The viewport the animation ends at.</param>
    /// <param name="duration">How long it runs.</param>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    public void BeginAnimation(ViewState target, TimeSpan duration, long timestamp)
    {
        lock (_sync)
        {
            _motionOrigin = _history.Current;
            _history.Motion = new ViewMotion(
                ViewMotionKind.Animated, target, timestamp, timestamp + Ticks(duration));
        }

        Stats.RecordDestination();
        DestinationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reports that a fling started at <paramref name="timestamp"/> that the
    /// host expects to move the view centre by
    /// (<paramref name="travelX"/>, <paramref name="travelY"/>) EPSG:3857
    /// metres over <paramref name="duration"/>. The travel is scaled by
    /// <see cref="FlingScale"/>, and the resting viewport is predicted from the
    /// current view.
    /// </summary>
    /// <param name="travelX">Expected easting travel, metres.</param>
    /// <param name="travelY">Expected northing travel, metres.</param>
    /// <param name="duration">How long the fling runs.</param>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    /// <returns>The predicted resting viewport, or null before the host has reported a view.</returns>
    public ViewState? BeginFling(double travelX, double travelY, TimeSpan duration, long timestamp)
    {
        ViewState target;
        lock (_sync)
        {
            if (_history.Current is not { } current)
            {
                return null;
            }

            target = current with
            {
                CenterX = current.CenterX + (travelX * _flingScale),
                CenterY = current.CenterY + (travelY * _flingScale),
            };
            _motionOrigin = current;
            _history.Motion = new ViewMotion(
                ViewMotionKind.Fling, target, timestamp, timestamp + Ticks(duration));
        }

        Stats.RecordDestination();
        DestinationChanged?.Invoke(this, EventArgs.Empty);
        return target;
    }

    /// <summary>Sets the route and own-ship inputs.</summary>
    /// <param name="inputs">The inputs.</param>
    public void SetInputs(PredictionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        lock (_sync)
        {
            _inputs = inputs;
        }
    }

    /// <summary>The demand plan at <paramref name="timestamp"/>.</summary>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    /// <returns>The plan.</returns>
    public DemandPlan Plan(long timestamp)
    {
        lock (_sync)
        {
            EndMotionIfDone(timestamp);
            if (!_history.IsMoving(timestamp))
            {
                _history.NoteIdle(timestamp);
            }

            return new DemandPlan(
                _history.Current,
                _history.Motion,
                _model.Predict(_history, _inputs, timestamp));
        }
    }

    /// <summary>Plans at the current time.</summary>
    /// <returns>The plan.</returns>
    public DemandPlan Plan() => Plan(Stopwatch.GetTimestamp());

    // Under _sync.
    private void EndMotionIfDone(long timestamp)
    {
        var motion = _history.Motion;
        if (motion.Kind == ViewMotionKind.Idle)
        {
            return;
        }

        if (motion.Kind == ViewMotionKind.Animated)
        {
            // An animation ends on its target, or soon after its time is up
            // (a limit stopped it, or a gesture took over).
            var arrived = motion.Target is { } target && _history.Current is { } current && Near(current, target);
            if (!arrived && timestamp < motion.ArrivalTimestamp + Ticks(ArrivalGrace))
            {
                return;
            }
        }
        else
        {
            // A fling ends when the view rests after its time is up; it may
            // pass the predicted resting place on the way. One still moving
            // well after its time has been taken over by a drag.
            var resting = !_history.IsMoving(timestamp);
            if (timestamp < motion.ArrivalTimestamp
                || (!resting && timestamp < motion.ArrivalTimestamp + (4 * Ticks(ArrivalGrace))))
            {
                return;
            }

            // A fling that came to rest teaches the planner how far flings go.
            if (resting
                && motion.Target is { } predicted
                && _motionOrigin is { } origin
                && _history.Current is { } rest)
            {
                var predictedTravel = Distance(origin, predicted);
                var actualTravel = Distance(origin, rest);
                if (predictedTravel > 0 && double.IsFinite(actualTravel))
                {
                    Stats.RecordFling(Distance(rest, predicted) / predictedTravel);
                    var observed = Math.Clamp(actualTravel / (predictedTravel / _flingScale), MinFlingScale, MaxFlingScale);
                    _flingScale = ((1 - FlingScaleAlpha) * _flingScale) + (FlingScaleAlpha * observed);
                }
            }
        }

        _history.Motion = ViewMotion.Idle;
        _motionOrigin = null;
    }

    private static bool Near(ViewState a, ViewState b)
    {
        var tolerance = Math.Max(a.Resolution, b.Resolution);
        return Math.Abs(a.CenterX - b.CenterX) <= tolerance
            && Math.Abs(a.CenterY - b.CenterY) <= tolerance
            && Math.Abs(a.Resolution - b.Resolution) <= 1e-6 * Math.Max(a.Resolution, b.Resolution);
    }

    private static double Distance(ViewState a, ViewState b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);
}

/// <summary>
/// What the view needs next (map rendering slice&#160;9): the current view,
/// the reported motion and the predicted views.
/// </summary>
/// <param name="Current">The current view, or null before the host has reported one.</param>
/// <param name="Motion">The reported motion.</param>
/// <param name="Views">The predicted views, most likely first.</param>
public sealed record DemandPlan(ViewState? Current, ViewMotion Motion, IReadOnlyList<PredictedView> Views)
{
    /// <summary>
    /// The views tile demand covers: a known destination, and the motion's
    /// views within <see cref="DemandPlanner.TileHorizon"/>.
    /// </summary>
    public IEnumerable<PredictedView> TileViews =>
        Views.Where(v => v.Source == PredictionSource.Destination
            || (v.Source == PredictionSource.Motion && v.Horizon <= DemandPlanner.TileHorizon));

    /// <summary>
    /// The views cell demand covers: everything within
    /// <see cref="DemandPlanner.CellHorizon"/>, plus a known destination and
    /// history whenever they are.
    /// </summary>
    public IEnumerable<PredictedView> CellViews =>
        Views.Where(v => v.Source is PredictionSource.Destination or PredictionSource.History
            || v.Horizon <= DemandPlanner.CellHorizon);
}
