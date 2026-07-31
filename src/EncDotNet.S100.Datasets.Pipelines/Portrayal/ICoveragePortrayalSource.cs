namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Capability implemented by coverage dataset processors that can produce a
/// Mapsui-free <see cref="CoveragePortrayalResult"/> — the coverage analogue
/// of <see cref="IVectorPortrayalSource"/>.
/// </summary>
public interface ICoveragePortrayalSource
{
    /// <summary>
    /// Builds an immutable, Mapsui-free snapshot of this dataset's coverage
    /// portrayal for the supplied render context. Implementations run under a
    /// render gate and materialise the styled coverage (including the selected
    /// time step) so the result is safe to convert to Mapsui layers off the
    /// gate.
    /// </summary>
    /// <param name="context">Optional render context (palette, mariner, time step).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Mapsui-free coverage portrayal result.</returns>
    Task<CoveragePortrayalResult> BuildCoveragePortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default);
}
