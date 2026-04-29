namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Mariner-configurable display preferences used by S-100 portrayal rules
/// (S-100 Part 9 §4.2 — "Mariner Selections"). These values are independent
/// of the current display viewport and are typically persisted across
/// sessions per user preference.
/// </summary>
public sealed class MarinerSettings
{
    /// <summary>Safety contour depth in metres.</summary>
    public double SafetyContour { get; init; } = 30.0;

    /// <summary>Safety depth in metres (for sounding selection).</summary>
    public double SafetyDepth { get; init; } = 30.0;

    /// <summary>Shallow contour depth in metres.</summary>
    public double ShallowContour { get; init; } = 2.0;

    /// <summary>Deep contour depth in metres.</summary>
    public double DeepContour { get; init; } = 30.0;

    /// <summary>Default mariner settings (safety contour 30 m, etc.).</summary>
    public static MarinerSettings Default { get; } = new();
}
