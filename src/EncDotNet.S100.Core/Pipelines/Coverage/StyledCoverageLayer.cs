namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Bundles sampled coverage data with its portrayal rules,
/// ready to be handed to a renderer.
/// </summary>
public sealed class StyledCoverageLayer
{
    /// <summary>The sampled coverage grid data.</summary>
    public required SampledCoverage Coverage { get; init; }

    /// <summary>The color scheme that maps values to colours.</summary>
    public required CoverageColorScheme ColorScheme { get; init; }

    /// <summary>The value treated as no-data in the coverage grid.</summary>
    public required float NoDataValue { get; init; }

    /// <summary>
    /// Georeferencer that maps grid cells to native CRS coordinates.
    /// Carries the source CRS identifier needed for reprojection.
    /// </summary>
    public required GridGeoreferencer Georeferencer { get; init; }
}
