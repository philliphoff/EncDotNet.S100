using EncDotNet.S100.Interoperability;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// The Mapsui-side pairing of a built <see cref="ILayer"/> with its
/// renderer-neutral <see cref="SubLayerStackItem"/>. Since issue #398 the S-98
/// ordering / suppression engine operates on <see cref="SubLayerStackItem"/>
/// (in the Mapsui-free <c>Datasets.Pipelines</c> assembly); this wrapper lets
/// the viewer keep a stable <c>CurrentStackEntries</c> shape while the actual
/// sort / suppression decision lives once, on the neutral item.
/// </summary>
/// <remarks>
/// The S-98 plane / priority / source metadata is exposed as delegating
/// properties over <see cref="Item"/> so existing consumers (the Layer Stack
/// view-model, pick ranking) continue to read them off the entry unchanged.
/// </remarks>
/// <param name="Layer">The Mapsui layer to be drawn.</param>
/// <param name="Item">
/// The renderer-neutral stack item the S-98 engine ordered / suppressed. Its
/// <see cref="SubLayerStackItem.Payload"/> is the original portrayal slice this
/// <paramref name="Layer"/> was built from (or a suppression-filtered copy).
/// </param>
public sealed record LayerStackEntry(ILayer Layer, SubLayerStackItem Item)
{
    /// <summary>The S-98 display plane this layer lives in (S-98 Annex A §4.4.1).</summary>
    public S98DisplayPlane Plane => Item.Plane;

    /// <summary>Intra-plane ordering hint, ascending — lower draws first.</summary>
    public int WithinPlanePriority => Item.WithinPlanePriority;

    /// <summary>Stable identifier for the source dataset.</summary>
    public string SourceDatasetId => Item.SourceDatasetId;

    /// <summary>Optional feature type code for a per-feature-type slice.</summary>
    public string? SourceFeatureType => Item.SourceFeatureType;

    /// <summary>Optional IC-declared custom plane identifier (S-98 Annex A §A-3.2.1.1).</summary>
    public string? ExtensionId => Item.ExtensionId;
}
