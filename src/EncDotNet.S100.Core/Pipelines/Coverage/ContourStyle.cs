namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Defines the visual style for a depth contour line.
/// </summary>
public sealed class ContourStyle
{
    /// <summary>The depth value this contour represents, in metres.</summary>
    public required float Depth { get; init; }

    /// <summary>Line width in display units.</summary>
    public required float LineWidth { get; init; }

    /// <summary>Line colour as a hex string (e.g. "#333333").</summary>
    public required string Color { get; init; }

    /// <summary>
    /// Dash pattern. <c>null</c> or empty means a solid line.
    /// Values alternate between dash length and gap length.
    /// </summary>
    public float[]? DashPattern { get; init; }

    /// <summary>Whether to label this contour line with its depth value.</summary>
    public bool ShowLabel { get; init; }
}
