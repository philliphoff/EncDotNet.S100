using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The vector portrayal results a <see cref="MapsuiDatasetRenderer"/> has
/// converted, per processor, keyed by everything that shapes them. A processor
/// builds a new result on every portrayal, so two maps showing one dataset
/// would each keep their own copy of its instruction slices, geometry provider,
/// feature tags and coverage. When a fresh result matches one another map
/// already shows, the renderer converts that one instead, and the fresh copy is
/// dropped.
/// </summary>
/// <remarks>
/// <para>
/// The processor still portrays on every render, so its own per-render state
/// (the time intervals, drawing priorities and depth adjustment it reports) is
/// unchanged; only which equal result the map keeps differs.
/// </para>
/// <para>
/// Results are held weakly. A result stays cached while a map's chart stack
/// holds it, and drops out once no map shows it.
/// </para>
/// <para>
/// Callers hold the processor's render gate, so one processor's entries are
/// never read and written at the same time.
/// </para>
/// </remarks>
internal sealed class SharedPortrayalCache
{
    private readonly ConditionalWeakTable<IDatasetProcessor, Dictionary<string, WeakReference<VectorPortrayalResult>>> _byProcessor = new();

    /// <summary>
    /// The key for <paramref name="result"/>, or <see langword="null"/> when it
    /// can't be shared: no content scope identifies the dataset's content.
    /// </summary>
    /// <param name="result">The fresh portrayal result.</param>
    /// <param name="styleStateHashes">
    /// Each sub-layer's style-state hash (its instructions, palette and
    /// scales), in sub-layer order.
    /// </param>
    /// <returns>The key, or <see langword="null"/>.</returns>
    public static string? KeyFor(VectorPortrayalResult result, IReadOnlyList<string?> styleStateHashes)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(styleStateHashes);

        if (string.IsNullOrEmpty(result.ContentScope) || styleStateHashes.Count != result.SubLayers.Count)
            return null;

        // The content scope covers the dataset bytes and the catalogue content,
        // so everything the processor derives from them alone (geometry,
        // feature tags, coverage, extents, the asset providers). The
        // style-state hashes cover each sub-layer's instructions, so the
        // display state, time, hidden features, depth adjustment and mariner
        // settings that chose them, and the palette and scales. The rest is
        // every other value the renderer or the chart stack reads.
        var key = new StringBuilder();
        Append(key, result.Product);
        Append(key, result.Spec.ToString());
        Append(key, result.SourceDatasetId);
        Append(key, result.ContentScope);
        Append(key, result.Info);
        Append(key, Format(result.SymbolScale));
        Append(key, Format(result.TextScale));
        Append(key, Format(result.OutOfBandMinDisplayScale));
        Append(key, result.PreferLiveDrawing ? "live" : "tiled");
        Append(key, Format(result.CellMinimumDisplayScale));
        Append(key, Format(result.CellCompilationScale));
        Append(key, Format(result.CellMaximumDisplayScale));
        Append(key, result.LayerNames is null ? null : string.Join('\u001e', result.LayerNames));
        for (var i = 0; i < result.SubLayers.Count; i++)
        {
            var hash = styleStateHashes[i];
            if (hash is null)
                return null;

            var sub = result.SubLayers[i];
            Append(key, sub.LayerKey);
            Append(key, sub.LayerName);
            Append(key, sub.Plane.ToString());
            Append(key, sub.WithinPlanePriority.ToString(CultureInfo.InvariantCulture));
            Append(key, sub.SourceFeatureType);
            Append(key, sub.PatternClipCacheKey);
            Append(key, sub.ApplyOutOfBandCap ? "cap" : "nocap");
            Append(key, sub.LegendCaption);
            Append(key, sub.LegendTitle);
            Append(key, Format(sub.Opacity));
            Append(key, hash);
        }

        return key.ToString();
    }

    /// <summary>
    /// Finds a live result built for <paramref name="key"/> that matches
    /// <paramref name="fresh"/> where the key can't look (the sub-layer legends).
    /// </summary>
    /// <param name="processor">The processor the result portrays.</param>
    /// <param name="key">The result's key.</param>
    /// <param name="fresh">The fresh result.</param>
    /// <param name="shared">The shared result, when found.</param>
    /// <returns><see langword="true"/> when a result was found.</returns>
    public bool TryGet(IDatasetProcessor processor, string key, VectorPortrayalResult fresh, out VectorPortrayalResult shared)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(fresh);

        if (_byProcessor.TryGetValue(processor, out var results)
            && results.TryGetValue(key, out var weak)
            && weak.TryGetTarget(out var found)
            && LegendsMatch(found, fresh))
        {
            shared = found;
            return true;
        }

        shared = null!;
        return false;
    }

    /// <summary>Records <paramref name="result"/> as the result for <paramref name="key"/>.</summary>
    /// <param name="processor">The processor the result portrays.</param>
    /// <param name="key">The result's key.</param>
    /// <param name="result">The result.</param>
    public void Put(IDatasetProcessor processor, string key, VectorPortrayalResult result)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(result);

        var results = _byProcessor.GetValue(processor, static _ => new(StringComparer.Ordinal));

        // Drop entries whose result no map shows any more, so a processor
        // rendered in many style states doesn't keep a key for each.
        List<string>? dead = null;
        foreach (var (existing, weak) in results)
        {
            if (!weak.TryGetTarget(out _))
                (dead ??= []).Add(existing);
        }

        if (dead is not null)
        {
            foreach (var existing in dead)
                results.Remove(existing);
        }

        results[key] = new WeakReference<VectorPortrayalResult>(result);
    }

    /// <summary>The number of keys held for <paramref name="processor"/>, live or not. For tests.</summary>
    internal int CountFor(IDatasetProcessor processor) =>
        _byProcessor.TryGetValue(processor, out var results) ? results.Count : 0;

    private static bool LegendsMatch(VectorPortrayalResult a, VectorPortrayalResult b)
    {
        for (var i = 0; i < a.SubLayers.Count; i++)
        {
            var left = a.SubLayers[i].Legend;
            var right = b.SubLayers[i].Legend;
            if (left is null || right is null)
            {
                if (left is not null || right is not null)
                    return false;
            }
            else if (!left.SequenceEqual(right))
            {
                return false;
            }
        }

        return true;
    }

    // Each field is length-prefixed, so no value can be mistaken for another
    // value's boundary.
    private static void Append(StringBuilder key, string? value)
    {
        if (value is null)
        {
            key.Append("-;");
            return;
        }

        key.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string? Format(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
