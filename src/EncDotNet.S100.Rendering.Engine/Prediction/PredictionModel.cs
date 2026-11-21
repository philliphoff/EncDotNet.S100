using System.Diagnostics;

namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>
/// Predicts the views a host will show next (map rendering slice&#160;9).
/// Called whenever the demand planner plans; must be cheap and must not block.
/// </summary>
public interface IPredictionModel
{
    /// <summary>Returns weighted future views, most likely first.</summary>
    /// <param name="history">What the view has done recently, with the host's reported motion.</param>
    /// <param name="inputs">Route and own-ship inputs.</param>
    /// <param name="timestamp">The <see cref="Stopwatch"/> timestamp to predict from.</param>
    /// <returns>The predicted views; empty when nothing is expected.</returns>
    IReadOnlyList<PredictedView> Predict(ViewHistory history, PredictionInputs inputs, long timestamp);
}

/// <summary>
/// The default prediction model, strongest signal first: a known destination,
/// the motion of the view (centre velocity and zoom rate), the route ahead
/// while following own ship, then recent places and the start view.
/// </summary>
public sealed class PredictionModel : IPredictionModel
{
    /// <summary>Probability of an animation's target: the host asked for it.</summary>
    public const double AnimationProbability = 0.95;

    /// <summary>Probability of a fling's resting viewport: an estimate of where the coast stops.</summary>
    public const double FlingProbability = 0.8;

    /// <summary>Probability of a recent place or the start view.</summary>
    public const double HistoryProbability = 0.1;

    /// <summary>How far ahead, in seconds, the motion is extrapolated, with each step's probability.</summary>
    private static readonly (double Seconds, double Probability)[] MotionSteps =
    [
        (0.5, 0.6),
        (1.0, 0.45),
        (2.0, 0.25),
        (4.0, 0.1),
    ];

    /// <summary>Route points used, nearest first, and the probability of the first.</summary>
    private const int MaxRoutePoints = 4;
    private const double RouteProbability = 0.5;

    // Below this many view widths per second the view counts as still.
    private const double MinViewsPerSecond = 0.05;

    // A zoom slower than this (natural log of resolution per second) counts as none.
    private const double MinZoomRate = 0.05;

    // The motion is never extrapolated beyond a zoom of this factor.
    private const double MaxZoomFactor = 16.0;

    /// <inheritdoc />
    public IReadOnlyList<PredictedView> Predict(ViewHistory history, PredictionInputs inputs, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(inputs);
        if (history.Current is not { } current)
        {
            return [];
        }

        var views = new List<PredictedView>();

        // 1. A known destination.
        var motion = history.Motion;
        if (motion.HasTarget)
        {
            var remaining = Stopwatch.GetElapsedTime(timestamp, motion.ArrivalTimestamp);
            views.Add(new PredictedView(
                motion.Target!.Value,
                motion.Kind == ViewMotionKind.Fling ? FlingProbability : AnimationProbability,
                remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                PredictionSource.Destination));
        }

        // 2. Motion: the pan velocity and zoom rate, while a gesture is in
        // progress and its end isn't known.
        else if (history.IsMoving(timestamp))
        {
            var speed = Math.Sqrt((history.VelocityX * history.VelocityX) + (history.VelocityY * history.VelocityY));
            var viewSpan = Math.Max(current.Width, current.Height) * current.Resolution;
            var panning = viewSpan > 0 && speed / viewSpan >= MinViewsPerSecond;
            var zooming = Math.Abs(history.ZoomRate) >= MinZoomRate;
            if (panning || zooming)
            {
                foreach (var (seconds, probability) in MotionSteps)
                {
                    var zoom = zooming
                        ? Math.Clamp(Math.Exp(history.ZoomRate * seconds), 1.0 / MaxZoomFactor, MaxZoomFactor)
                        : 1.0;
                    var view = current with
                    {
                        CenterX = panning ? current.CenterX + (history.VelocityX * seconds) : current.CenterX,
                        CenterY = panning ? current.CenterY + (history.VelocityY * seconds) : current.CenterY,
                        Resolution = current.Resolution * zoom,
                    };
                    views.Add(new PredictedView(view, probability, TimeSpan.FromSeconds(seconds), PredictionSource.Motion));
                }
            }
        }

        // 3. The route ahead of own ship, while following it.
        if (inputs.RouteAhead is { Count: > 0 } route)
        {
            var speed = inputs.OwnShipSpeed is > 0 and var s && double.IsFinite(s) ? s : (double?)null;
            var probability = RouteProbability;
            var count = Math.Min(route.Count, MaxRoutePoints);
            for (var i = 0; i < count; i++)
            {
                var point = route[i];
                var distance = Math.Sqrt(
                    ((point.X - current.CenterX) * (point.X - current.CenterX))
                    + ((point.Y - current.CenterY) * (point.Y - current.CenterY)));
                var horizon = speed is { } v
                    ? TimeSpan.FromSeconds(Math.Min(distance / v, TimeSpan.FromDays(1).TotalSeconds))
                    : TimeSpan.FromSeconds(2 * (i + 1));
                views.Add(new PredictedView(
                    current with { CenterX = point.X, CenterY = point.Y },
                    probability,
                    horizon,
                    PredictionSource.Route));
                probability *= 0.7;
            }
        }

        // 4. History: places the view rested at, then the start view.
        foreach (var place in history.RecentPlaces)
        {
            if (!ViewHistory.SamePlace(place, current))
            {
                views.Add(new PredictedView(place, HistoryProbability, TimeSpan.Zero, PredictionSource.History));
            }
        }

        if (history.Start is { } start
            && !ViewHistory.SamePlace(start, current)
            && !history.RecentPlaces.Any(p => ViewHistory.SamePlace(p, start)))
        {
            views.Add(new PredictedView(start, HistoryProbability, TimeSpan.Zero, PredictionSource.History));
        }


        return views;
    }
}
