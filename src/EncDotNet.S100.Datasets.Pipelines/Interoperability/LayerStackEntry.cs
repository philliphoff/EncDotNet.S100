using EncDotNet.S100.Interoperability;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// One layer's slot in the S-98 cross-dataset stack. A
/// <see cref="LayerStackEntry"/> carries everything the
/// <see cref="IInteroperabilityAuthority"/> needs to sort layers
/// across all loaded datasets without taking a dependency on the
/// viewer's <c>DatasetEntry</c> shape.
/// </summary>
/// <param name="Layer">The Mapsui layer to be drawn.</param>
/// <param name="Plane">
/// The S-98 display plane this layer lives in (S-98 Annex A §4.4.1 +
/// §A-3.2.1.1). Assigned by the producing processor, possibly
/// overridden later by an Interoperability Catalogue rule (PR-L2).
/// </param>
/// <param name="WithinPlanePriority">
/// Intra-plane ordering hint, ascending — lower draws first. For
/// vector products this is the S-100 Part 9 §10
/// <c>drawingPriority</c> exposed by the processor's display list;
/// for coverage products it is a processor-chosen integer (e.g.
/// arrows above colour band).
/// </param>
/// <param name="SourceDatasetId">
/// Stable identifier for the source dataset (typically the
/// dataset's file name or exchange-set relative path). Used as a
/// tiebreaker for stable sorting and for diagnostics.
/// </param>
/// <param name="SourceFeatureType">
/// Optional feature type code when the entry represents a
/// per-feature-type slice (e.g. S-101 area-fill split). Null for
/// whole-layer entries. PR-L2 IC rules consume this for
/// suppression / replacement decisions.
/// </param>
/// <param name="ExtensionId">
/// Optional IC-declared custom plane identifier (S-98 Annex A
/// §A-3.2.1.1 allows catalogues to declare non-canonical planes).
/// When set, the authority slots the entry between canonical
/// planes by the catalogue's <c>order</c> attribute rather than by
/// <paramref name="Plane"/>. Always null for PR-L1; the field is
/// the escape hatch resolved in PR-L0 TBD-9.
/// </param>
public sealed record LayerStackEntry(
    ILayer Layer,
    S98DisplayPlane Plane,
    int WithinPlanePriority,
    string SourceDatasetId,
    string? SourceFeatureType = null,
    string? ExtensionId = null);
