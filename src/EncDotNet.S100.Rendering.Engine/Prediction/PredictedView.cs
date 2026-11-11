namespace EncDotNet.S100.Rendering.Engine.Prediction;

/// <summary>Which signal a <see cref="PredictedView"/> comes from, strongest first.</summary>
public enum PredictionSource
{
    /// <summary>A fling's resting viewport or an animation's target.</summary>
    Destination = 0,

    /// <summary>The view extrapolated along its recent pan velocity and zoom rate.</summary>
    Motion = 1,

    /// <summary>A point on the next legs of the active route, while following own ship.</summary>
    Route = 2,

    /// <summary>A place the view rested at recently, or the start view.</summary>
    History = 3,
}

/// <summary>
/// A view the prediction model expects the host to show (map rendering
/// slice&#160;9), with how likely it is and how soon.
/// </summary>
/// <param name="View">The predicted view.</param>
/// <param name="Probability">How likely the view is to be shown, 0 to 1.</param>
/// <param name="Horizon">How long from now until it would be shown.</param>
/// <param name="Source">The signal the prediction comes from.</param>
public readonly record struct PredictedView(
    ViewState View,
    double Probability,
    TimeSpan Horizon,
    PredictionSource Source);

/// <summary>An EPSG:3857 position, in metres.</summary>
/// <param name="X">Easting.</param>
/// <param name="Y">Northing.</param>
public readonly record struct WorldPoint(double X, double Y);

/// <summary>
/// Inputs to the prediction model beyond the view's own history (map
/// rendering slice&#160;9).
/// </summary>
public sealed record PredictionInputs
{
    /// <summary>No extra inputs.</summary>
    public static PredictionInputs None { get; } = new();

    /// <summary>
    /// Points along the next legs of the active route ahead of own ship,
    /// nearest first, while the view follows own ship; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<WorldPoint>? RouteAhead { get; init; }

    /// <summary>
    /// Own ship's speed over ground in metres per second, used to time the
    /// route points; <see langword="null"/> when unknown.
    /// </summary>
    public double? OwnShipSpeed { get; init; }
}
