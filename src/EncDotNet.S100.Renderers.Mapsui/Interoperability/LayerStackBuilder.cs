using System.Collections.Generic;
using System.Linq;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Collects each loaded dataset's <see cref="LayerStackEntry"/>
/// values and sorts the whole stack via an
/// <see cref="IInteroperabilityAuthority"/>. The builder is a thin
/// orchestration layer; the actual policy lives in the authority.
/// </summary>
/// <remarks>
/// <para>
/// Callers (typically the viewer's dataset loader) walk their
/// per-dataset entries in <em>paint order top-first</em> — the
/// reverse of the layer-collection order Mapsui paints. The builder
/// flattens that view into a single list preserving the load-order
/// tiebreaker inside each plane.
/// </para>
/// <para>
/// PR-L1 contract: <see cref="Build"/> returns layers in
/// bottom-of-stack-first order (lower indices in the layer
/// collection are painted earlier). That matches
/// <c>MapsuiMapHost.ReorderDatasetLayers</c>'s existing contract.
/// </para>
/// </remarks>
public static class LayerStackBuilder
{
    /// <summary>
    /// Sorts every dataset's stack entries through
    /// <paramref name="authority"/> and returns the resulting Mapsui
    /// layers in bottom-of-stack-first paint order.
    /// </summary>
    /// <param name="authority">The interoperability authority that
    /// supplies the sort policy.</param>
    /// <param name="datasetEntries">
    /// Per-dataset stack-entry slices in <em>paint order top-first</em>
    /// — entry 0 is the top of the UI's dataset list (drawn last).
    /// The builder reverses the outer order so the authority's
    /// stable sort sees the dataset that should win ties at the
    /// <em>bottom</em> of the sort input, leaving the topmost dataset
    /// last in the output for any tied (plane, priority) group.
    /// </param>
    public static IReadOnlyList<LayerStackEntry> Build(
        IInteroperabilityAuthority authority,
        IReadOnlyList<IReadOnlyList<LayerStackEntry>> datasetEntries)
    {
        System.ArgumentNullException.ThrowIfNull(authority);
        System.ArgumentNullException.ThrowIfNull(datasetEntries);

        // Walk datasets from bottom-of-UI up so the within-plane
        // tiebreaker preserves the user's paint expectation: the
        // entry at the top of the Datasets panel wins ties (its
        // layer paints last, i.e. on top).
        var flat = new List<LayerStackEntry>();
        for (int i = datasetEntries.Count - 1; i >= 0; i--)
        {
            flat.AddRange(datasetEntries[i]);
        }

        return authority.Sort(flat);
    }

    /// <summary>
    /// Convenience: projects a sorted list of entries to a flat
    /// list of <see cref="ILayer"/> in the same order — what
    /// <c>MapsuiMapHost.ReorderDatasetLayers</c> consumes.
    /// </summary>
    public static List<ILayer> ToLayerList(IEnumerable<LayerStackEntry> sortedEntries)
    {
        System.ArgumentNullException.ThrowIfNull(sortedEntries);
        return sortedEntries.Select(e => e.Layer).ToList();
    }
}
