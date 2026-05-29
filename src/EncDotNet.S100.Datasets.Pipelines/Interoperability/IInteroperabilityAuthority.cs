using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Cross-dataset interoperability decision point: assigns a default
/// S-98 display plane to a (product, feature-or-layer-kind) pair and
/// sorts the global stack of <see cref="LayerStackEntry"/> values
/// into S-98-shaped paint order.
/// </summary>
/// <remarks>
/// <para>
/// PR-L1 ships a fixed-table S-98 implementation
/// (<see cref="InteroperabilityAuthority"/>) and a strict
/// load-order alternative
/// (<see cref="LoadOrderInteroperabilityAuthority"/>). Hosts wire a
/// chosen implementation through DI as an
/// <see cref="IInteroperabilityAuthorityProvider"/>; consumers
/// (e.g. <c>DatasetLoaderService</c>, GML dataset processors)
/// receive that provider via constructor injection and consult
/// <see cref="IInteroperabilityAuthorityProvider.Current"/> on each
/// operation so a runtime swap (e.g. a viewer setting flip from
/// S-98 to strict load-order) takes effect immediately. PR-L1
/// deliberately ships <em>no</em> IC parsing,
/// suppression, replacement, or hybridisation; those Level 1+
/// behaviours are owned by PR-L2 once the IHO publishes a
/// normative S-100 Part 16 IC schema and a viewer-side catalogue
/// reader is in place.
/// </para>
/// <para>
/// The contract is pure — <c>(default plane lookup)</c> and
/// <c>(in entries → out entries)</c>; the implementation must be
/// thread-safe and free of viewer dependencies so it can be
/// unit-tested in isolation.
/// </para>
/// </remarks>
public interface IInteroperabilityAuthority
{
    /// <summary>
    /// Returns the default S-98 display plane for a given product
    /// specification and optional feature-type or sub-layer kind
    /// hint. The <paramref name="featureTypeOrLayerKind"/> argument
    /// lets callers distinguish e.g. S-111 arrow overlay
    /// (<c>"s111.arrows"</c>) from S-111 station glyphs
    /// (<c>"s111.stations"</c>); pass <c>null</c> for products with a
    /// single layer.
    /// </summary>
    /// <param name="productSpec">
    /// The product spec name (e.g. <c>"S-101"</c>, <c>"S-102"</c>).
    /// </param>
    /// <param name="featureTypeOrLayerKind">
    /// Optional sub-layer kind or feature type code. Recognised values
    /// are documented on <see cref="InteroperabilityAuthority"/>.
    /// </param>
    /// <returns>
    /// The default plane for the pair. Unknown products fall back to
    /// <see cref="S98DisplayPlane.OtherChartOverlays"/> with a
    /// debug warning (S-98 v2.0.0 Annex A §4.1.1 — "other similar
    /// products may also be covered on a case-by-case basis").
    /// </returns>
    S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null);

    /// <summary>
    /// Sorts the supplied entries into S-98-shaped paint order.
    /// The result is a new list — the input is not mutated.
    /// </summary>
    /// <remarks>
    /// Sort is stable in the input order: entries with equal plane
    /// and within-plane priority preserve their relative position,
    /// which the caller (the dataset loader) sets to its
    /// load-order tiebreaker.
    /// </remarks>
    IReadOnlyList<LayerStackEntry> Sort(IEnumerable<LayerStackEntry> entries);

    /// <summary>
    /// Applies S-98 inter-product rules to a sorted layer stack.
    /// Each rule's <see cref="S98InteroperabilityRule.Condition"/>
    /// is checked against the supplied
    /// <paramref name="loadedDatasets"/> (plus optional mariner
    /// settings); rules whose condition fires invoke their
    /// <see cref="S98InteroperabilityRule.Effect"/> on the stack.
    /// Rules execute in the enumeration order of
    /// <paramref name="rules"/> — each rule's output is the next
    /// rule's input.
    /// </summary>
    /// <param name="sortedStack">
    /// The layer stack as produced by <see cref="Sort"/>. The input
    /// is not mutated; rules return new lists.
    /// </param>
    /// <param name="loadedDatasets">
    /// Snapshot of every dataset currently known to the loader.
    /// Rules inspect product specs and active flags to decide
    /// whether to fire.
    /// </param>
    /// <param name="mariner">
    /// Optional active mariner-settings snapshot. Rules that depend
    /// on viewer settings (e.g. R-101-102-B's safety-contour
    /// exception per MSC.232(82) §5.8) read it; rules that don't
    /// ignore it. <c>null</c> falls back to
    /// <see cref="EncDotNet.S100.Pipelines.MarinerSettings.Default"/>.
    /// </param>
    /// <param name="rules">
    /// Optional rule collection. Defaults to
    /// <see cref="S98DefaultRules.Default"/>. Passing an empty
    /// collection is a deliberate no-op (rules disabled).
    /// </param>
    /// <returns>
    /// The post-rule layer stack. Layers may be replaced (suppressed
    /// features removed) but the count is monotonic per rule —
    /// rules cannot add new entries in v1.
    /// </returns>
    IReadOnlyList<LayerStackEntry> ApplyRules(
        IReadOnlyList<LayerStackEntry> sortedStack,
        IReadOnlyList<LoadedDatasetInfo> loadedDatasets,
        EncDotNet.S100.Pipelines.MarinerSettings? mariner = null,
        IReadOnlyCollection<S98InteroperabilityRule>? rules = null);
}
