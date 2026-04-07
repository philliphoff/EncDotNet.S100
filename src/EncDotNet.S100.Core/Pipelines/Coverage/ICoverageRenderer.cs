namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Renders a <see cref="StyledCoverageLayer"/> to a platform-specific output.
/// </summary>
/// <typeparam name="TOutput">The rendering output type (e.g. SKBitmap, byte[], etc.).</typeparam>
public interface ICoverageRenderer<TOutput>
{
    /// <summary>
    /// Renders the styled coverage layer to the output type.
    /// </summary>
    /// <param name="layer">The styled coverage layer to render.</param>
    /// <param name="viewport">The viewport defining the display area.</param>
    TOutput Render(StyledCoverageLayer layer, Viewport viewport);
}
