using System.Collections.Generic;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Result of converting a dataset processor's Mapsui-free portrayal output
/// (<c>IVectorPortrayalSource</c> / <c>ICoveragePortrayalSource</c>) into
/// Mapsui layers. Produced by <see cref="EncDotNet.S100.Renderers.Mapsui.MapsuiDatasetRenderer"/>.
/// </summary>
/// <remarks>
/// This type lives in the Mapsui renderer assembly (it carries Mapsui
/// <see cref="ILayer"/> / <see cref="MRect"/> types) but keeps the
/// <c>EncDotNet.S100.Datasets.Pipelines</c> namespace so existing Mapsui-aware
/// consumers (the viewer, the pipeline tests) resolve it without churn.
/// </remarks>
public sealed class DatasetResult
{
    /// <summary>The Mapsui layers to draw, in paint order.</summary>
    public required IReadOnlyList<ILayer> Layers { get; init; }

    /// <summary>The dataset's EPSG:3857 extent.</summary>
    public required MRect Extent { get; init; }

    /// <summary>Human-readable status line describing the dataset.</summary>
    public required string Info { get; init; }

    /// <summary>The product specification (name + edition) of the rendered dataset.</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>
    /// Optional stable per-layer keys for the viewer's per-sub-layer
    /// disclosure UI, parallel by index to <see cref="Layers"/>. Processors
    /// that emit more than one sub-layer (e.g. S-101 areas + line work,
    /// S-111 colour band + arrows) populate this so the UI can show
    /// per-sub-layer toggles; single-layer products leave it null. When
    /// non-null, the list length must match <see cref="Layers"/>.
    /// </summary>
    public IReadOnlyList<string>? LayerNames { get; init; }

    /// <summary>
    /// S-98 cross-dataset stack metadata, parallel by index to
    /// <see cref="Layers"/> when supplied (every entry's
    /// <see cref="LayerStackEntry.Layer"/> appears in <see cref="Layers"/>
    /// exactly once and at the same index). The viewer's dataset loader pumps
    /// every loaded dataset's entries through
    /// <see cref="LayerStackBuilder"/> to compute the global paint order
    /// across products (S-98 Annex A §4.4.1; S-98 Main §9.2.1).
    /// </summary>
    public IReadOnlyList<LayerStackEntry>? StackEntries { get; init; }
}
