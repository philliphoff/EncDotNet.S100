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
/// PR-L1 ships a fixed-table implementation
/// (<see cref="InteroperabilityAuthority"/>) — there is <em>no</em>
/// IC parsing, suppression, replacement, or hybridisation in this
/// PR. Those Level 1+ behaviours are owned by PR-L2 once the IHO
/// publishes a normative S-100 Part 16 IC schema and a viewer-side
/// catalogue reader is in place.
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
    /// lets callers distinguish e.g. S-111 colour-band layer
    /// (<c>"s111.color-band"</c>) from S-111 arrow overlay
    /// (<c>"s111.arrows"</c>); pass <c>null</c> for products with a
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
}
