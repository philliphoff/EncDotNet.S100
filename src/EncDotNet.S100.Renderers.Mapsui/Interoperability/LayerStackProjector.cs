using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Maps the renderer-neutral, S-98-ordered / suppressed
/// <see cref="SubLayerStackItem"/> list produced by the engine
/// (<see cref="LayerStackBuilder.Build"/> + <see cref="IInteroperabilityAuthority.ApplyRules"/>)
/// back onto the Mapsui <see cref="ILayer"/> objects the viewer paints, without
/// re-running the expensive display-list rasterisation for suppressed layers.
/// </summary>
/// <remarks>
/// <para>
/// Issue #398: the S-98 engine now lives in the Mapsui-free
/// <c>Datasets.Pipelines</c> assembly and operates on
/// <see cref="SubLayerStackItem"/>. The viewer builds its <see cref="ILayer"/>s
/// once at load time (pattern-fill clipping is multi-second), so re-rasterising
/// a suppression-filtered sub-layer would be both slow and a regression risk.
/// </para>
/// <para>
/// Instead, this projector reuses each item's prebuilt <see cref="ILayer"/> and,
/// when the engine replaced a vector payload's <see cref="VectorSubLayer"/>
/// (R-101-102-B depth suppression), records the features it dropped, found by
/// comparing the original and surviving instruction <c>FeatureReference</c>
/// sets, as the entry's <see cref="LayerStackEntry.SuppressedFeatures"/>: a
/// filter on the layer's scene ops that the chart tile applies while
/// rasterising (S-98 Annex A §8.4.1; MSC.232(82) §5.8).
/// </para>
/// </remarks>
public static class LayerStackProjector
{
    /// <summary>
    /// Returns the stable <c>(dataset id, sub-layer key)</c> join key used to
    /// match a neutral <see cref="SubLayerStackItem"/> back to its prebuilt
    /// Mapsui layer.
    /// </summary>
    /// <param name="item">The stack item to key.</param>
    /// <returns>The composite lookup key.</returns>
    public static (string DatasetId, string LayerKey) KeyOf(SubLayerStackItem item)
    {
        System.ArgumentNullException.ThrowIfNull(item);
        return (item.SourceDatasetId, LayerKeyOf(item.Payload));
    }

    private static string LayerKeyOf(StackPayload payload) => payload switch
    {
        VectorStackPayload v => v.SubLayer.LayerKey,
        CoverageStackPayload c => c.SubLayer.LayerKey,
        SyntheticStackPayload s => s.LayerKey,
        _ => string.Empty,
    };

    /// <summary>
    /// Projects the S-98-ruled item list onto prebuilt Mapsui layers.
    /// </summary>
    /// <param name="ruledItems">
    /// The ordered, suppression-applied items from the engine.
    /// </param>
    /// <param name="prebuilt">
    /// Lookup from <see cref="KeyOf"/> to the prebuilt <see cref="LayerStackEntry"/>
    /// (its <see cref="LayerStackEntry.Layer"/> is the rasterised layer and its
    /// <see cref="LayerStackEntry.Item"/> carries the <em>original</em>,
    /// un-suppressed payload).
    /// </param>
    /// <param name="rebuildCoverage">
    /// Optional callback that rebuilds a coverage sub-layer the engine replaced
    /// (e.g. an S-104 surface with a land-area mask attached). When
    /// omitted, or when it returns <see langword="null"/>, the prebuilt layer
    /// is reused.
    /// </param>
    /// <returns>
    /// One <see cref="LayerStackEntry"/> per input item, wrapping the reused (or
    /// suppression-filtered) <see cref="ILayer"/> and the ruled item. Items with
    /// no prebuilt layer are skipped.
    /// </returns>
    public static IReadOnlyList<LayerStackEntry> Project(
        IReadOnlyList<SubLayerStackItem> ruledItems,
        IReadOnlyDictionary<(string DatasetId, string LayerKey), LayerStackEntry> prebuilt,
        Func<CoverageSubLayerBase, ILayer?>? rebuildCoverage = null) =>
        Project(
            ruledItems,
            prebuilt,
            rebuildCoverage is null ? null : (_, subLayer) => rebuildCoverage(subLayer));

    /// <summary>
    /// Projects the S-98-ruled item list onto prebuilt Mapsui layers, passing
    /// the ruled item to <paramref name="rebuildCoverage"/> so the caller can
    /// rebuild with per-dataset state (the item's
    /// <see cref="SubLayerStackItem.SourceDatasetId"/>).
    /// </summary>
    internal static IReadOnlyList<LayerStackEntry> Project(
        IReadOnlyList<SubLayerStackItem> ruledItems,
        IReadOnlyDictionary<(string DatasetId, string LayerKey), LayerStackEntry> prebuilt,
        Func<SubLayerStackItem, CoverageSubLayerBase, ILayer?>? rebuildCoverage)
    {
        System.ArgumentNullException.ThrowIfNull(ruledItems);
        System.ArgumentNullException.ThrowIfNull(prebuilt);

        var result = new List<LayerStackEntry>(ruledItems.Count);
        foreach (var item in ruledItems)
        {
            if (!prebuilt.TryGetValue(KeyOf(item), out var pre))
                continue;

            var (layer, suppressed) = ProjectOne(item, pre, rebuildCoverage);
            result.Add(new LayerStackEntry(layer, item) { SuppressedFeatures = suppressed });
        }
        return result;
    }

    private static (ILayer Layer, IReadOnlySet<string>? Suppressed) ProjectOne(
        SubLayerStackItem ruled,
        LayerStackEntry prebuilt,
        Func<SubLayerStackItem, CoverageSubLayerBase, ILayer?>? rebuildCoverage)
    {
        // Coverage payloads are re-rasterised (not feature-filtered) when the
        // engine replaced the sub-layer — e.g. R-101-104-B attaches a land-area
        // mask to the S-104 surface so it can be clipped to water (issue #483),
        // and R-111-WX-B blanks model current arrows inside S-111 (issue #1228).
        if (ruled.Payload is CoverageStackPayload ruledCoverage
            && prebuilt.Item.Payload is CoverageStackPayload originalCoverage
            && !ReferenceEquals(ruledCoverage.SubLayer, originalCoverage.SubLayer)
            && ruledCoverage.SubLayer is GridCoverageSubLayer or IsolineCoverageSubLayer or ArrowCoverageSubLayer
            && rebuildCoverage is not null)
        {
            var rebuilt = rebuildCoverage(ruled, ruledCoverage.SubLayer);
            if (rebuilt is null)
                return (prebuilt.Layer, null);

            // The rebuilt layer is a fresh ILayer that defaults to
            // Enabled=true / Opacity=1 with no visible range. Carry over the
            // prebuilt layer's current display state so a rule-triggered rebuild
            // (e.g. R-101-104-B land clipping) never re-shows a hidden surface
            // — including the default-hidden S-104 surface (issue #483) — or
            // resets the user's opacity / scale-window choices.
            CopyDisplayState(prebuilt.Layer, rebuilt);
            return (rebuilt, null);
        }

        // Only vector payloads are suppressible (R-101-102-B). If the engine
        // left the sub-layer reference untouched, nothing is suppressed.
        if (ruled.Payload is not VectorStackPayload ruledVector
            || prebuilt.Item.Payload is not VectorStackPayload originalVector
            || ReferenceEquals(ruledVector.SubLayer, originalVector.SubLayer))
        {
            return (prebuilt.Layer, null);
        }

        // The prebuilt layer stays on the stack; the features the rule
        // dropped become a filter on its scene's ops (map rendering slice 8),
        // applied by the chart tile and the single-surface renderer alike.
        var dropped = ComputeDroppedRefs(originalVector.SubLayer, ruledVector.SubLayer);
        return (prebuilt.Layer, dropped.Count == 0 ? null : dropped);
    }

    private static void CopyDisplayState(ILayer source, ILayer target)
    {
        target.Enabled = source.Enabled;
        target.Opacity = source.Opacity;

        // MinVisible / MaxVisible are only settable on the concrete BaseLayer
        // (read-only on ILayer). Coverage renderers return BaseLayer-derived
        // layers, so carry the scale window over when both sides are BaseLayers.
        if (source is BaseLayer sourceBase && target is BaseLayer targetBase)
        {
            targetBase.MinVisible = sourceBase.MinVisible;
            targetBase.MaxVisible = sourceBase.MaxVisible;
        }
    }

    private static HashSet<string> ComputeDroppedRefs(VectorSubLayer original, VectorSubLayer surviving)
    {
        var survivors = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var instruction in surviving.Instructions)
        {
            if (instruction.FeatureReference is { Length: > 0 } r)
                survivors.Add(r);
        }

        var dropped = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var instruction in original.Instructions)
        {
            if (instruction.FeatureReference is { Length: > 0 } r && !survivors.Contains(r))
                dropped.Add(r);
        }
        return dropped;
    }

    /// <summary>
    /// Flattens an ordered <see cref="LayerStackEntry"/> list into the bare
    /// <see cref="ILayer"/> list the map host consumes, preserving order.
    /// </summary>
    /// <param name="entries">The ordered stack entries.</param>
    /// <returns>The layers in the same order.</returns>
    public static List<ILayer> ToLayerList(IEnumerable<LayerStackEntry> entries)
    {
        System.ArgumentNullException.ThrowIfNull(entries);
        return entries.Select(e => e.Layer).ToList();
    }
}
