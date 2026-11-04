using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Renders one registered exchange-set cell off the map, only so the expensive
/// parts of loading it land in the disk caches (#1266): the Lua portrayal in
/// the portrayal cache and the pattern-fill clip in the pattern-clip cache.
/// The layers are thrown away and the processor is dropped afterwards.
/// </summary>
/// <remarks>
/// <para>
/// The cell is opened the way the loader opens it
/// (<see cref="DatasetLoaderService.CreateExchangeSetCellProcessor"/>) and
/// rendered under the map's current presentation, so the cache keys match the
/// ones an interactive load of the cell asks for.
/// </para>
/// <para>
/// The <paramref name="factory"/> and <paramref name="renderer"/> passed in
/// should be the warmer's own: they share the viewer's catalogue managers and
/// write through the <paramref name="recorder"/>'s wrappers of the viewer's
/// disk caches, with a Lua engine of their own. Every processor gets its own
/// portrayal catalogue and Lua contexts, so this never shares Lua state with a
/// load on the map.
/// </para>
/// </remarks>
internal sealed class ExchangeSetCellPortrayer
{
    private readonly DatasetPipelineFactory _factory;
    private readonly MapsuiDatasetRenderer _renderer;
    private readonly PortrayalWarmRecorder _recorder;
    private readonly Func<MapPresentationState> _presentation;

    /// <summary>Creates the portrayer.</summary>
    /// <param name="factory">Builds the cell's processor; its portrayal cache must be <paramref name="recorder"/>'s.</param>
    /// <param name="renderer">Renders the processor; its pattern-clip cache must be <paramref name="recorder"/>'s.</param>
    /// <param name="recorder">Notes the cache keys each render uses.</param>
    /// <param name="presentation">The map's current presentation (mariner settings, display modes).</param>
    public ExchangeSetCellPortrayer(
        DatasetPipelineFactory factory,
        MapsuiDatasetRenderer renderer,
        PortrayalWarmRecorder recorder,
        Func<MapPresentationState> presentation)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(presentation);
        _factory = factory;
        _renderer = renderer;
        _recorder = recorder;
        _presentation = presentation;
    }

    /// <summary>
    /// Renders <paramref name="entry"/> on a pool thread and returns the cache
    /// keys it used, or <see langword="null"/> when the cell used no cache (it
    /// is not a vector product portrayed through the cache).
    /// </summary>
    /// <param name="entry">A registered exchange-set cell. Read on the calling thread only.</param>
    /// <param name="cancellationToken">Stops the render at its next check.</param>
    /// <returns>The keys, or <see langword="null"/> for a cell this does not apply to.</returns>
    public Task<IReadOnlyList<string>?> PortrayAsync(DatasetEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Source is not { } source || entry.RelativePath is not { } relativePath)
            return Task.FromResult<IReadOnlyList<string>?>(null);

        // DatasetEntry is UI-thread state: read what the render needs here.
        var updates = entry.UpdateRelativePaths.ToArray();
        var spec = entry.ProductSpec;
        var presentation = _presentation();

        return Task.Run(async () =>
        {
            var processor = DatasetLoaderService.CreateExchangeSetCellProcessor(
                _factory, source, relativePath, updates, spec);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var recording = _recorder.BeginRecording();
                var result = await _renderer.RenderAsync(
                    processor,
                    presentation.CreateRenderContext(processor),
                    cancellationToken).ConfigureAwait(false);
                foreach (var layer in result.Layers)
                    (layer as IDisposable)?.Dispose();
                var keys = recording.Keys;
                return keys.Count == 0 ? null : keys;
            }
            catch (NotSupportedException)
            {
                // Neither a vector nor a coverage product: nothing to cache.
                return null;
            }
            finally
            {
                (processor as IDisposable)?.Dispose();
            }
        }, cancellationToken);
    }
}
