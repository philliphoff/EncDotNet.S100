using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// One sub-layer's slot in the S-98 cross-dataset stack — the renderer-neutral
/// successor to the Mapsui-tied <c>LayerStackEntry</c>. A
/// <see cref="SubLayerStackItem"/> carries everything the
/// <see cref="IInteroperabilityAuthority"/> needs to order and suppress
/// sub-layers across all loaded datasets, plus a
/// <see cref="StackPayload"/> a renderer turns into a drawable layer, with no
/// dependency on any rendering backend (issue #398).
/// </summary>
/// <param name="Payload">
/// The Mapsui-free portrayal slice to draw (vector instructions or a coverage
/// sub-layer). S-98 suppression may replace this with a filtered payload.
/// </param>
/// <param name="Plane">
/// The S-98 display plane this sub-layer lives in (S-98 Annex A §4.4.1 +
/// §A-3.2.1.1). Assigned by the producing processor, possibly overridden by an
/// Interoperability Catalogue rule.
/// </param>
/// <param name="WithinPlanePriority">
/// Intra-plane ordering hint, ascending — lower draws first. For vector
/// products this is the S-100 Part 9 §10 <c>drawingPriority</c>; for coverage
/// products a processor-chosen integer (e.g. arrows above colour band).
/// </param>
/// <param name="SourceDatasetId">
/// Stable identifier for the source dataset (typically the file name or
/// exchange-set relative path). Used as the stable-sort tiebreaker and the
/// suppression join key against <see cref="LoadedDatasetInfo"/>.
/// </param>
/// <param name="SourceFeatureType">
/// Optional feature type code when the item is a per-feature-type slice (e.g.
/// the S-101 area / line split). Null for whole-layer items.
/// </param>
/// <param name="ExtensionId">
/// Optional IC-declared custom plane identifier (S-98 Annex A §A-3.2.1.1).
/// When set, the authority slots the item by the catalogue's <c>order</c>
/// rather than by <paramref name="Plane"/>. Always null for PR-L1.
/// </param>
public sealed record SubLayerStackItem(
    StackPayload Payload,
    S98DisplayPlane Plane,
    int WithinPlanePriority,
    string SourceDatasetId,
    string? SourceFeatureType = null,
    string? ExtensionId = null);
