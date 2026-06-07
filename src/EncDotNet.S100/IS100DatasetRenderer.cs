using System.Threading;
using System.Threading.Tasks;

namespace EncDotNet.S100;

/// <summary>
/// Renders an S-100 <see cref="S100Layer"/> to a result of type
/// <typeparamref name="TResult"/>. The result type is a parameter so the same
/// abstraction can produce encoded image bytes (<see cref="PngS100DatasetRenderer"/>),
/// and — in future — richer results such as bitmaps or Mapsui layer collections,
/// without changing the contract.
/// </summary>
/// <typeparam name="TResult">The rendered result type (e.g. <c>byte[]</c> of PNG data).</typeparam>
public interface IS100DatasetRenderer<TResult>
{
    /// <summary>Renders a single layer.</summary>
    /// <param name="layer">The dataset + portrayal lens to render.</param>
    /// <param name="options">Render options, or <c>null</c> for defaults.</param>
    /// <param name="cancellationToken">Cancellation token observed cooperatively.</param>
    /// <returns>The rendered result.</returns>
    /// <exception cref="System.NotSupportedException">
    /// The layer's dataset shape cannot be rendered to an image.
    /// </exception>
    Task<TResult> RenderAsync(
        S100Layer layer,
        S100RendererOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Convenience helpers over <see cref="IS100DatasetRenderer{TResult}"/>.
/// </summary>
public static class S100DatasetRendererExtensions
{
    /// <summary>
    /// Renders <paramref name="dataset"/> using the bundled feature and portrayal
    /// catalogues for its product specification — the one-call on-ramp.
    /// </summary>
    public static Task<TResult> RenderAsync<TResult>(
        this IS100DatasetRenderer<TResult> renderer,
        S100Dataset dataset,
        S100RendererOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentNullException.ThrowIfNull(renderer);
        System.ArgumentNullException.ThrowIfNull(dataset);
        return renderer.RenderAsync(new S100Layer { Dataset = dataset }, options, cancellationToken);
    }
}
