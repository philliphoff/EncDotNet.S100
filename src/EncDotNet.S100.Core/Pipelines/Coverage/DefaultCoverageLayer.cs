namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Default implementation of <see cref="ICoverageLayer"/> produced by <see cref="CoveragePipeline"/>.
/// </summary>
internal sealed class DefaultCoverageLayer : ICoverageLayer
{
    public required CoverageMetadata Metadata { get; init; }
    public required BoundingBox Extent { get; init; }
    public required GridMetadata Grid { get; init; }
    public required IReadOnlyList<string?> CellColors { get; init; }
    public required IReadOnlyList<ContourLine> Contours { get; init; }
}
