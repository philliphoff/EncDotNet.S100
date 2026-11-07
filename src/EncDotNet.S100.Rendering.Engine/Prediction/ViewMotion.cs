namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>How the view is moving, as far as the host knows.</summary>
public enum ViewMotionKind
{
    /// <summary>No gesture or animation the host knows of; the view may still be dragged.</summary>
    Idle = 0,

    /// <summary>
    /// A fling: the view coasts to a stop after the pointer is released. The
    /// resting viewport is an estimate (see <see cref="DemandPlanner.BeginFling"/>).
    /// </summary>
    Fling = 1,

    /// <summary>An animation to a known viewport, such as an animated zoom or a "go to".</summary>
    Animated = 2,
}

/// <summary>
/// The motion a host reports with its view (map rendering slice&#160;9): for a
/// fling or an animation, the viewport the view will come to rest at and when
/// it gets there. A known destination is the strongest signal the prediction
/// model has.
/// </summary>
/// <param name="Kind">The kind of motion.</param>
/// <param name="Target">The viewport the motion ends at, when known.</param>
/// <param name="StartTimestamp">The <see cref="System.Diagnostics.Stopwatch"/> timestamp the motion started at.</param>
/// <param name="ArrivalTimestamp">The <see cref="System.Diagnostics.Stopwatch"/> timestamp the motion ends at.</param>
public readonly record struct ViewMotion(
    ViewMotionKind Kind,
    ViewState? Target,
    long StartTimestamp,
    long ArrivalTimestamp)
{
    /// <summary>No known motion.</summary>
    public static ViewMotion Idle => default;

    /// <summary>Whether the motion has a known destination.</summary>
    public bool HasTarget => Kind != ViewMotionKind.Idle && Target is not null;
}
