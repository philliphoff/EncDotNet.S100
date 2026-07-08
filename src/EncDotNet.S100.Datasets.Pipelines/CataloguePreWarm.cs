using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pre-warms catalogue assets referenced by a list of drawing instructions
/// so the synchronous Skia / Mapsui renderer's resolver lambdas can read
/// from in-memory dicts without async I/O.
/// </summary>
/// <remarks>
/// Walks <see cref="DrawingInstruction"/> subtypes for unique
/// <c>SymbolReference</c> / <c>LineStyleReference</c> / <c>AreaFillReference</c>
/// / <c>OutlineStyleReference</c> names, awaits each catalogue
/// <c>Get*Async</c> call, and stores the result (or null on
/// <see cref="PortrayalAssetNotFoundException"/> — or any other lookup
/// failure) in the returned dicts. Renderer callers then close over those
/// dicts in their sync resolver lambdas.
/// </remarks>
internal static class CataloguePreWarm
{
    /// <summary>
    /// Pre-warms every symbol / line-style / area-fill referenced by the
    /// given instruction list against <paramref name="catalogue"/>.
    /// </summary>
    public static async Task<PreWarmResult> ForInstructionsAsync(
        IPortrayalAssetSource catalogue,
        IReadOnlyList<DrawingInstruction> instructions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(instructions);

        var symbolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lineNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var areaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ins in instructions)
        {
            switch (ins)
            {
                case PointInstruction p when !string.IsNullOrEmpty(p.SymbolReference):
                    symbolNames.Add(p.SymbolReference);
                    break;
                case LineInstruction l when !string.IsNullOrEmpty(l.LineStyleReference):
                    if (!IsSimpleLineStyle(l.LineStyleReference))
                        lineNames.Add(l.LineStyleReference);
                    break;
                case AreaInstruction a:
                    if (!string.IsNullOrEmpty(a.AreaFillReference))
                        areaNames.Add(a.AreaFillReference);
                    if (!string.IsNullOrEmpty(a.OutlineStyleReference) &&
                        !IsSimpleLineStyle(a.OutlineStyleReference))
                        lineNames.Add(a.OutlineStyleReference);
                    break;
            }
        }

        var symbols = new Dictionary<string, SvgSymbol?>(symbolNames.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var name in symbolNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { symbols[name] = await catalogue.GetSymbolAsync(name, cancellationToken).ConfigureAwait(false); }
            // Catch generously: a missing or malformed catalogue asset must
            // never block a render — the renderer treats null the same as
            // "not in catalogue" (the pre-PR-async behaviour).
            catch (Exception) { symbols[name] = null; }
        }

        var lineStyles = new Dictionary<string, LineStyle?>(lineNames.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var name in lineNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { lineStyles[name] = await catalogue.GetLineStyleAsync(name, cancellationToken).ConfigureAwait(false); }
            catch (Exception) { lineStyles[name] = null; }
        }

        var areaFills = new Dictionary<string, AreaFill?>(areaNames.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var name in areaNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { areaFills[name] = await catalogue.GetAreaFillAsync(name, cancellationToken).ConfigureAwait(false); }
            catch (Exception) { areaFills[name] = null; }
        }

        // Second-order pre-warm: pattern-fill area definitions reference
        // an SVG symbol via AreaFill.PatternSymbol that the renderer pulls
        // through the same SymbolProvider lambda. Walk every loaded
        // AreaFill for a non-empty PatternSymbol and pre-fetch it.
        foreach (var fill in areaFills.Values)
        {
            if (fill?.PatternSymbol is { Length: > 0 } sym && !symbols.ContainsKey(sym))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { symbols[sym] = await catalogue.GetSymbolAsync(sym, cancellationToken).ConfigureAwait(false); }
                catch (Exception) { symbols[sym] = null; }
            }
        }

        return new PreWarmResult(symbols, lineStyles, areaFills);
    }

    // The S-100 Part 9A Lua portrayal model emits LineInstruction /
    // AreaInstruction outline references with the inline "simple" line-style
    // sentinel (see LineInstruction.SimpleLineStyleReference). It is never a
    // named catalogue line style — its colour, width, and dash pattern travel
    // on the instruction itself — so resolving it always missed and threw a
    // KeyNotFoundException on every dataset load (#286). Skip it up front.
    private static bool IsSimpleLineStyle(string reference) =>
        string.Equals(reference, LineInstruction.SimpleLineStyleReference, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The outcome of a pre-warm pass: lookup-by-name dicts for symbols,
    /// line styles, and area fills (null entries indicate "not in catalogue").
    /// </summary>
    public sealed record PreWarmResult(
        IReadOnlyDictionary<string, SvgSymbol?> Symbols,
        IReadOnlyDictionary<string, LineStyle?> LineStyles,
        IReadOnlyDictionary<string, AreaFill?> AreaFills)
    {
        /// <summary>Sync resolver returning SVG content for the given symbol name, or null.</summary>
        public string? ResolveSymbolSvg(string name)
            => Symbols.TryGetValue(name, out var s) ? s?.SvgContent : null;

        /// <summary>Sync resolver returning the symbol record for the given name, or null.</summary>
        public SvgSymbol? ResolveSymbol(string name)
            => Symbols.TryGetValue(name, out var s) ? s : null;

        /// <summary>Sync resolver returning the line style for the given name, or null.</summary>
        public LineStyle? ResolveLineStyle(string name)
            => LineStyles.TryGetValue(name, out var s) ? s : null;

        /// <summary>Sync resolver returning the area fill for the given name, or null.</summary>
        public AreaFill? ResolveAreaFill(string name)
            => AreaFills.TryGetValue(name, out var s) ? s : null;
    }
}
