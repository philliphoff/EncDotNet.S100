namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Captures display state that influences portrayal rule selection:
/// current viewport, scale, safety parameters, and user preferences.
/// </summary>
public sealed class NavigationContext
{
    /// <summary>Current display viewport.</summary>
    public required Viewport Viewport { get; init; }

    /// <summary>Display scale denominator (e.g. 25_000 for 1:25000).</summary>
    public required double ScaleDenominator { get; init; }

    /// <summary>Safety contour depth in metres.</summary>
    public double SafetyContour { get; init; } = 30.0;

    /// <summary>Safety depth in metres (for sounding selection).</summary>
    public double SafetyDepth { get; init; } = 30.0;

    /// <summary>Shallow contour depth in metres.</summary>
    public double ShallowContour { get; init; } = 2.0;

    /// <summary>Deep contour depth in metres.</summary>
    public double DeepContour { get; init; } = 30.0;
}
