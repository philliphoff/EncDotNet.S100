using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Collects each loaded dataset's <see cref="SubLayerStackItem"/> values and
/// sorts the whole stack via an <see cref="IInteroperabilityAuthority"/>. The
/// builder is a thin orchestration layer; the actual policy lives in the
/// authority.
/// </summary>
/// <remarks>
/// <para>
/// Callers (the viewer's dataset loader, the headless compositor) walk their
/// per-dataset items in <em>paint order top-first</em> — the reverse of the
/// order the renderer paints. The builder flattens that view into a single
/// list preserving the load-order tiebreaker inside each plane.
/// </para>
/// <para>
/// <see cref="Build"/> returns items in bottom-of-stack-first order (lower
/// indices paint earlier).
/// </para>
/// </remarks>
public static class LayerStackBuilder
{
    /// <summary>
    /// Sorts every dataset's stack items through <paramref name="authority"/>
    /// and returns them in bottom-of-stack-first paint order.
    /// </summary>
    /// <param name="authority">The interoperability authority supplying the sort policy.</param>
    /// <param name="datasetItems">
    /// Per-dataset stack-item slices in <em>paint order top-first</em> — slice
    /// 0 is the top of the UI's dataset list (drawn last). The builder reverses
    /// the outer order so the authority's stable sort sees the dataset that
    /// should win ties at the <em>bottom</em> of the sort input, leaving the
    /// topmost dataset last in the output for any tied (plane, priority) group.
    /// </param>
    public static IReadOnlyList<SubLayerStackItem> Build(
        IInteroperabilityAuthority authority,
        IReadOnlyList<IReadOnlyList<SubLayerStackItem>> datasetItems)
    {
        System.ArgumentNullException.ThrowIfNull(authority);
        System.ArgumentNullException.ThrowIfNull(datasetItems);

        // Walk datasets from bottom-of-UI up so the within-plane tiebreaker
        // preserves the user's paint expectation: the entry at the top of the
        // Datasets panel wins ties (it paints last, i.e. on top).
        var flat = new List<SubLayerStackItem>();
        for (int i = datasetItems.Count - 1; i >= 0; i--)
        {
            flat.AddRange(datasetItems[i]);
        }

        return authority.Sort(flat);
    }
}
