namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Centralizes the concurrency-safe, one-shot colour-palette load protocol
/// shared by every portrayal catalogue.
/// </summary>
/// <remarks>
/// <para>
/// A single <see cref="IPortrayalAssetCache"/> is shared per product spec
/// across every dataset of that spec (for example, every cell of one S-101
/// exchange set), and those datasets load on separate threads. Two earlier
/// defects followed from loading palettes ad hoc:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Cache poisoning.</b> Marking <see cref="IPortrayalAssetCache.PalettesLoaded"/>
///       before the load completed meant a cancelled or aborted load (routine
///       in the viewer when a pan/zoom/reload cancels the in-flight render)
///       left the shared cache flagged loaded-but-empty, so every later palette
///       lookup failed.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Races.</b> Concurrent loaders wrote the non-thread-safe palette
///       dictionary simultaneously, or a second loader observed it
///       half-populated.
///     </description>
///   </item>
/// </list>
/// <para>
/// This helper serializes the load through
/// <see cref="IPortrayalAssetCache.PaletteLoadGate"/> with double-checked
/// locking and commits <see cref="IPortrayalAssetCache.PalettesLoaded"/> only
/// after the load runs to completion without being cancelled. See issue #321.
/// </para>
/// </remarks>
public static class PaletteLoadCoordinator
{
    /// <summary>
    /// Ensures the shared cache's palettes are loaded exactly once, safely
    /// under concurrency, retrying after a cancelled attempt rather than
    /// poisoning the cache.
    /// </summary>
    /// <param name="cache">The shared per-spec asset cache.</param>
    /// <param name="loadInto">
    /// Populates <see cref="IPortrayalAssetCache.Palettes"/>. Invoked at most
    /// once per successful load while the gate is held. Must not set
    /// <see cref="IPortrayalAssetCache.PalettesLoaded"/> itself.
    /// </param>
    /// <param name="onLoaded">
    /// Applies the catalogue's active-palette selection (typically Day). Called
    /// after the load completes and on the already-loaded fast path.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async ValueTask EnsureLoadedAsync(
        IPortrayalAssetCache cache,
        Func<CancellationToken, ValueTask> loadInto,
        Action onLoaded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(loadInto);
        ArgumentNullException.ThrowIfNull(onLoaded);

        if (cache.PalettesLoaded)
        {
            onLoaded();
            return;
        }

        await cache.PaletteLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-checked: another loader may have completed while we waited.
            if (cache.PalettesLoaded)
            {
                onLoaded();
                return;
            }

            await loadInto(cancellationToken).ConfigureAwait(false);

            // Commit only now that the load has run to completion without being
            // cancelled, so a cancelled attempt is retried on the next call.
            cache.PalettesLoaded = true;
            onLoaded();
        }
        finally
        {
            cache.PaletteLoadGate.Release();
        }
    }
}
