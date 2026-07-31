namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Capability implemented by vector dataset processors that can produce a
/// Mapsui-free <see cref="VectorPortrayalResult"/> — the portrayal-output
/// seam that lets the Mapsui renderer build the dataset's layers without the
/// processor (and therefore the headless-facing Pipelines assembly) taking a
/// dependency on Mapsui.
/// </summary>
/// <remarks>
/// Mirrors the <see cref="IHeadlessImageRenderer"/> capability pattern: the
/// concrete processors implement this alongside their headless Skia path,
/// and the Mapsui renderer feature-tests with
/// <c>processor is IVectorPortrayalSource</c>.
/// </remarks>
public interface IVectorPortrayalSource
{
    /// <summary>
    /// Builds an immutable, Mapsui-free snapshot of this dataset's vector
    /// portrayal for the supplied render context. Implementations run under
    /// the processor's render gate and snapshot all mutable portrayal state
    /// (palette, ECDIS display, asset pre-warm) so the result is safe to
    /// convert to Mapsui layers off the gate.
    /// </summary>
    /// <param name="context">Optional render context (palette, scales, ECDIS, mariner).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Mapsui-free vector portrayal result.</returns>
    Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
        RenderContext? context = null,
        CancellationToken cancellationToken = default);
}
