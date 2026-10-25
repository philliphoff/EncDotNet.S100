namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Defines the visual style for a contour line: a depth contour, an isobar,
/// or a line of equal wave height.
/// </summary>
public sealed class ContourStyle
{
    /// <summary>
    /// The value this contour represents, in <see cref="Unit"/>. Products that
    /// work in typed quantities convert where they build the contour (for
    /// example an S-102 depth contour uses <c>Depth.Metres</c> and <c>"m"</c>).
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// The unit of <see cref="Value"/> as a UCUM-style symbol (e.g. <c>"m"</c>,
    /// <c>"hPa"</c>, <c>"m/s"</c>), matching the units of the coverage field
    /// the contour is traced on.
    /// </summary>
    public required string Unit { get; init; }

    /// <summary>Line width in display units.</summary>
    public required float LineWidth { get; init; }

    /// <summary>Line colour as a hex string (e.g. "#333333").</summary>
    public required string Color { get; init; }

    /// <summary>
    /// Dash pattern. <c>null</c> or empty means a solid line.
    /// Values alternate between dash length and gap length.
    /// </summary>
    public float[]? DashPattern { get; init; }

    /// <summary>Whether to label this contour line with its value.</summary>
    public bool ShowLabel { get; init; }
}
