using System.Collections.Concurrent;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Per-palette cache of processed-SVG symbol entries and rasterised pattern
/// tiles produced by <see cref="MapsuiDisplayListRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both <see cref="MapsuiDisplayListRenderer"/>'s processed-SVG ("data URI"
/// string post <c>SvgProcessor.Process</c>) and rasterised pattern-tile PNG
/// caches were originally instance-scoped. Because every dataset processor
/// constructs a fresh renderer on each <c>Render()</c> call, every palette
/// toggle / time-step scrub / mariner-setting change re-paid the full SVG
/// processing + PNG rasterization cost.
/// </para>
/// <para>
/// Lifting these caches into a dataset-processor-owned
/// <see cref="MapsuiRenderAssetCache"/> instance lets re-renders of the same
/// dataset reuse that work. The cache is keyed by palette name (Day/Dusk/
/// Night) so flipping between palettes does not invalidate sibling-palette
/// entries: each palette gets its own slot, populated lazily on first
/// access.
/// </para>
/// <para>
/// Processed SVG output depends on the active <see cref="ColorPalette"/>
/// (the processor recolours fills/strokes), so per-palette segmentation is
/// required for correctness.
/// </para>
/// <para>
/// The dictionaries are <see cref="ConcurrentDictionary{TKey,TValue}"/> to
/// allow safe sharing if a future caller renders concurrently.
/// </para>
/// </remarks>
public sealed class MapsuiRenderAssetCache
{
    private readonly ConcurrentDictionary<string, PerPaletteCache> _byPalette =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a cached <see cref="MapsuiDisplayListRenderer.SymbolEntry"/>
    /// for <paramref name="symbolName"/> under the given palette, invoking
    /// <paramref name="factory"/> on miss.
    /// </summary>
    internal MapsuiDisplayListRenderer.SymbolEntry GetOrAddSymbol(
        ColorPalette? palette,
        string symbolName,
        out bool wasCached,
        Func<string, MapsuiDisplayListRenderer.SymbolEntry> factory)
    {
        var bucket = GetBucket(palette);
        if (bucket.Symbols.TryGetValue(symbolName, out var existing))
        {
            wasCached = true;
            return existing;
        }

        wasCached = false;
        var produced = factory(symbolName);
        // GetOrAdd here (instead of the simpler indexer) keeps a parallel
        // factory call from racing past a cached entry.
        return bucket.Symbols.GetOrAdd(symbolName, produced);
    }

    /// <summary>
    /// Returns the cached pattern-tile PNG bytes for <paramref name="fillName"/>
    /// under the given palette, invoking <paramref name="factory"/> on miss.
    /// </summary>
    internal byte[]? GetOrAddPatternTile(
        ColorPalette? palette,
        string fillName,
        out bool wasCached,
        Func<string, byte[]?> factory)
    {
        var bucket = GetBucket(palette);
        if (bucket.PatternTiles.TryGetValue(fillName, out var existing))
        {
            wasCached = true;
            return existing;
        }

        wasCached = false;
        var produced = factory(fillName);
        return bucket.PatternTiles.GetOrAdd(fillName, produced);
    }

    private PerPaletteCache GetBucket(ColorPalette? palette)
    {
        var key = palette?.Name ?? string.Empty;
        return _byPalette.GetOrAdd(key, _ => new PerPaletteCache());
    }

    private sealed class PerPaletteCache
    {
        public ConcurrentDictionary<string, MapsuiDisplayListRenderer.SymbolEntry> Symbols { get; }
            = new(StringComparer.OrdinalIgnoreCase);

        public ConcurrentDictionary<string, byte[]?> PatternTiles { get; }
            = new(StringComparer.OrdinalIgnoreCase);
    }
}
